using System.Text.Json;
using MicroClaw.Agent.Memory;
using MicroClaw.Skills;
using MicroClaw.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace MicroClaw.Agent.Endpoints;

/// <summary>
/// Agent REST API 端点：Agent CRUD、DNA 基因文件管理、MCP 工具列表、流式对话。
/// </summary>
public static class AgentEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // ── Agent CRUD ────────────────────────────────────────────────────────

        endpoints.MapGet("/agents", (AgentStore store) =>
            Results.Ok(store.All.Select(ToDto)))
            .WithTags("Agents");

        endpoints.MapGet("/agents/{id}", (string id, AgentStore store) =>
        {
            AgentConfig? agent = store.GetById(id);
            return agent is null ? Results.NotFound() : Results.Ok(ToDto(agent));
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents", (AgentCreateRequest req, AgentStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { success = false, message = "Name is required.", errorCode = "BAD_REQUEST" });

            AgentConfig config = new(
                Id: string.Empty,
                Name: req.Name.Trim(),
                SystemPrompt: req.SystemPrompt ?? string.Empty,
                IsEnabled: req.IsEnabled,
                BoundSkillIds: req.BoundSkillIds ?? [],
                McpServers: req.McpServers ?? [],
                ToolGroupConfigs: [],
                CreatedAtUtc: DateTimeOffset.UtcNow,
                ContextWindowMessages: req.ContextWindowMessages);

            try
            {
                AgentConfig created = store.Add(config);
                return Results.Ok(new { created.Id });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message, errorCode = "AGENT_NAME_CONFLICT" });
            }
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/update", (AgentUpdateRequest req, AgentStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });

            AgentConfig? existing = store.GetById(req.Id);
            if (existing is null)
                return Results.NotFound(new { success = false, message = $"Agent '{req.Id}' not found.", errorCode = "NOT_FOUND" });

            AgentConfig updated = existing with
            {
                Name = req.Name?.Trim() ?? existing.Name,
                SystemPrompt = req.SystemPrompt ?? existing.SystemPrompt,
                IsEnabled = req.IsEnabled ?? existing.IsEnabled,
                BoundSkillIds = req.BoundSkillIds ?? existing.BoundSkillIds,
                McpServers = req.McpServers ?? existing.McpServers,
                ContextWindowMessages = req.ContextWindowMessages ?? existing.ContextWindowMessages,
            };

            try
            {
                AgentConfig? result = store.Update(req.Id, updated);
                return result is null
                    ? Results.NotFound(new { success = false, message = $"Agent '{req.Id}' not found.", errorCode = "NOT_FOUND" })
                    : Results.Ok(new { result.Id });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message, errorCode = "AGENT_NAME_CONFLICT" });
            }
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/delete", (AgentDeleteRequest req, AgentStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });

            AgentConfig? agent = store.GetById(req.Id);
            if (agent is null)
                return Results.NotFound(new { success = false, message = $"Agent '{req.Id}' not found.", errorCode = "NOT_FOUND" });
            if (agent.IsDefault)
                return Results.BadRequest(new { success = false, message = "Cannot delete the default agent.", errorCode = "BAD_REQUEST" });

            store.Delete(req.Id);
            return Results.Ok();
        })
        .WithTags("Agents");

        // ── DNA 基因文件管理 ─────────────────────────────────────────────────

        endpoints.MapGet("/agents/{id}/dna", (string id, AgentStore store, DNAService dna) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            return Results.Ok(dna.List(id));
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/{id}/dna", (string id, GeneFileWriteRequest req, AgentStore store, DNAService dna) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });
            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { success = false, message = "FileName is required.", errorCode = "BAD_REQUEST" });

            // 防止路径穿越：sanitize category 和 fileName
            string safeName = Path.GetFileName(req.FileName);
            string safeCategory = SanitizeCategory(req.Category);

            GeneFile file = dna.Write(id, safeCategory, safeName, req.Content ?? string.Empty);
            return Results.Ok(file);
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/{id}/dna/delete", (string id, GeneFileDeleteRequest req, AgentStore store, DNAService dna) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { message = $"Agent '{id}' not found." });

            string safeName = Path.GetFileName(req.FileName ?? string.Empty);
            bool deleted = dna.Delete(id, SanitizeCategory(req.Category), safeName);
            return deleted ? Results.Ok() : Results.NotFound();
        })
        .WithTags("Agents");

        // ── 工具列表（内置分组 + MCP 分组，含启用状态）────────────────────

        endpoints.MapGet("/agents/{id}/tools", async (string id, AgentStore store, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            AgentConfig? agent = store.GetById(id);
            if (agent is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            var groups = new List<object>();

            // ── 内置分组：cron ──────────────────────────────────────────────
            ToolGroupConfig? cronCfg = agent.ToolGroupConfigs.FirstOrDefault(g => g.GroupId == "cron");
            bool cronGroupEnabled = cronCfg is null || cronCfg.IsEnabled;
            groups.Add(new
            {
                id = "cron",
                name = "定时任务",
                type = "builtin",
                isEnabled = cronGroupEnabled,
                tools = CronTools.GetToolDescriptions().Select(t => new
                {
                    name = t.Name,
                    description = t.Description,
                    isEnabled = cronGroupEnabled && (cronCfg is null || !cronCfg.DisabledToolNames.Contains(t.Name))
                }).ToList()
            });

            // ── MCP Server 分组 ──────────────────────────────────────────────
            foreach (McpServerConfig srv in agent.McpServers)
            {
                ToolGroupConfig? srvCfg = agent.ToolGroupConfigs.FirstOrDefault(g => g.GroupId == srv.Name);
                bool srvEnabled = srvCfg is null || srvCfg.IsEnabled;

                if (!srvEnabled)
                {
                    groups.Add(new
                    {
                        id = srv.Name,
                        name = srv.Name,
                        type = "mcp",
                        isEnabled = false,
                        tools = Array.Empty<object>()
                    });
                    continue;
                }

                // 仅在分组启用时连接 MCP Server 加载工具列表
                var (tools, connections) = await ToolRegistry.LoadToolsAsync([srv], loggerFactory, ct);
                try
                {
                    groups.Add(new
                    {
                        id = srv.Name,
                        name = srv.Name,
                        type = "mcp",
                        isEnabled = true,
                        tools = tools.Select(t => new
                        {
                            name = t.Name,
                            description = t.Description,
                            isEnabled = srvCfg is null || !srvCfg.DisabledToolNames.Contains(t.Name)
                        }).ToList()
                    });
                }
                finally
                {
                    foreach (IAsyncDisposable conn in connections)
                        try { await conn.DisposeAsync(); } catch { }
                }
            }

            return Results.Ok(new { groups });
        })
        .WithTags("Agents");

        // ── 更新工具分组启用配置 ─────────────────────────────────────────────

        endpoints.MapPost("/agents/{id}/tools/settings", (string id, IReadOnlyList<ToolGroupConfigRequest> req, AgentStore store) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            IReadOnlyList<ToolGroupConfig> configs = req
                .Select(r => new ToolGroupConfig(r.GroupId, r.IsEnabled, r.DisabledToolNames ?? []))
                .ToList()
                .AsReadOnly();

            AgentConfig? updated = store.UpdateToolGroupConfigs(id, configs);
            return updated is null ? Results.NotFound() : Results.Ok(new { updated.Id });
        })
        .WithTags("Agents");

        // ── 技能绑定管理 ─────────────────────────────────────────────────────

        endpoints.MapGet("/agents/{id}/skills", (string id, AgentStore store) =>
        {
            AgentConfig? agent = store.GetById(id);
            return agent is null ? Results.NotFound() : Results.Ok(agent.BoundSkillIds);
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/{id}/skills", (string id, AgentBoundSkillsRequest req, AgentStore store, SkillStore skillStore) =>
        {
            AgentConfig? existing = store.GetById(id);
            if (existing is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            // 0-B-6: 批量校验 SkillId 是否存在于数据库
            IReadOnlyList<string> skillIds = req.SkillIds ?? [];
            List<string> invalidIds = skillIds.Where(sid => skillStore.GetById(sid) is null).ToList();
            if (invalidIds.Count > 0)
                return Results.BadRequest(new
                {
                    success = false,
                    message = $"Skill(s) not found: {string.Join(", ", invalidIds)}",
                    errorCode = "SKILL_NOT_FOUND"
                });

            AgentConfig updated = existing with { BoundSkillIds = skillIds };
            AgentConfig? result = store.Update(id, updated);
            return result is null
                ? Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" })
                : Results.Ok(new { result.Id });
        })
        .WithTags("Agents");

        return endpoints;
    }

    private static async Task WriteSseAsync(HttpResponse response, string data, CancellationToken ct)
    {
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static string SanitizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return string.Empty;
        // 按分隔符拆分再用 GetFileName 去掉任何路径穿越段
        return string.Join("/",
            category.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(Path.GetFileName)
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
    }

    private static object ToDto(AgentConfig a) => new
    {
        a.Id,
        a.Name,
        a.SystemPrompt,
        a.IsEnabled,
        a.BoundSkillIds,
        a.McpServers,
        a.ToolGroupConfigs,
        a.CreatedAtUtc,
        a.IsDefault,
        a.ContextWindowMessages,
    };
}

// ── Request records ──────────────────────────────────────────────────────────

public sealed record AgentCreateRequest(
    string Name,
    string? SystemPrompt,
    bool IsEnabled = true,
    IReadOnlyList<string>? BoundSkillIds = null,
    IReadOnlyList<McpServerConfig>? McpServers = null,
    int? ContextWindowMessages = null);

public sealed record AgentUpdateRequest(
    string Id,
    string? Name = null,
    string? SystemPrompt = null,
    bool? IsEnabled = null,
    IReadOnlyList<string>? BoundSkillIds = null,
    IReadOnlyList<McpServerConfig>? McpServers = null,
    int? ContextWindowMessages = null);

public sealed record AgentBoundSkillsRequest(IReadOnlyList<string>? SkillIds);

public sealed record AgentDeleteRequest(string Id);

public sealed record GeneFileWriteRequest(
    string FileName,
    string? Category,
    string? Content);

public sealed record GeneFileDeleteRequest(
    string FileName,
    string? Category);

public sealed record ToolGroupConfigRequest(
    string GroupId,
    bool IsEnabled,
    IReadOnlyList<string>? DisabledToolNames = null);
