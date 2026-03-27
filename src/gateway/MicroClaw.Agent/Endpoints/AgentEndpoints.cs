using System.Text.Json;
using MicroClaw.Agent.Memory;
using MicroClaw.Channels;
using MicroClaw.Skills;
using MicroClaw.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace MicroClaw.Agent.Endpoints;

/// <summary>
/// Agent REST API 端点：Agent CRUD、MCP Server 引用管理、工具列表、流式对话。
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

        endpoints.MapPost("/agents", (AgentCreateRequest req, AgentStore store, AgentDnaService agentDna) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { success = false, message = "Name is required.", errorCode = "BAD_REQUEST" });

            AgentConfig config = new(
                Id: string.Empty,
                Name: req.Name.Trim(),
                Description: req.Description ?? string.Empty,
                IsEnabled: req.IsEnabled,
                BoundSkillIds: req.BoundSkillIds ?? [],
                EnabledMcpServerIds: req.EnabledMcpServerIds ?? [],
                ToolGroupConfigs: [],
                CreatedAtUtc: DateTimeOffset.UtcNow,
                ContextWindowMessages: req.ContextWindowMessages,
                ExposeAsA2A: req.ExposeAsA2A);

            try
            {
                AgentConfig created = store.Add(config);
                agentDna.InitializeAgent(created.Id);
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
                Description = req.Description ?? existing.Description,
                IsEnabled = req.IsEnabled ?? existing.IsEnabled,
                BoundSkillIds = req.BoundSkillIds ?? existing.BoundSkillIds,
                EnabledMcpServerIds = req.EnabledMcpServerIds ?? existing.EnabledMcpServerIds,
                ContextWindowMessages = req.ContextWindowMessages ?? existing.ContextWindowMessages,
                ExposeAsA2A = req.ExposeAsA2A ?? existing.ExposeAsA2A,
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

        endpoints.MapPost("/agents/delete", (AgentDeleteRequest req, AgentStore store, AgentDnaService agentDna) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });

            AgentConfig? agent = store.GetById(req.Id);
            if (agent is null)
                return Results.NotFound(new { success = false, message = $"Agent '{req.Id}' not found.", errorCode = "NOT_FOUND" });
            if (agent.IsDefault)
                return Results.BadRequest(new { success = false, message = "Cannot delete the default agent.", errorCode = "BAD_REQUEST" });

            store.Delete(req.Id);
            agentDna.DeleteAgentFiles(req.Id);
            return Results.Ok();
        })
        .WithTags("Agents");

        // ── 全局 MCP Server 引用管理 ────────────────────────────────────────────────────────────────────────────

        endpoints.MapGet("/agents/{id}/mcp-servers", (string id, AgentStore store) =>
        {
            AgentConfig? agent = store.GetById(id);
            return agent is null
                ? Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" })
                : Results.Ok(agent.EnabledMcpServerIds);
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/{id}/mcp-servers", (string id, AgentMcpServersRequest req, AgentStore store, McpServerConfigStore mcpStore) =>
        {
            AgentConfig? existing = store.GetById(id);
            if (existing is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            IReadOnlyList<string> mcpIds = req.McpServerIds ?? [];
            List<string> invalidIds = mcpIds.Where(mid => mcpStore.GetById(mid) is null).ToList();
            if (invalidIds.Count > 0)
                return Results.BadRequest(new
                {
                    success = false,
                    message = $"MCP Server(s) not found: {string.Join(", ", invalidIds)}",
                    errorCode = "MCP_SERVER_NOT_FOUND"
                });

            AgentConfig? result = store.UpdateEnabledMcpServerIds(id, mcpIds);
            return result is null
                ? Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" })
                : Results.Ok(new { result.Id });
        })
        .WithTags("Agents");

        // ── 工具列表（内置分组 + 全局 MCP 分组，含启用状态）────────────────────

        endpoints.MapGet("/agents/{id}/tools", async (string id, AgentStore store, McpServerConfigStore mcpStore, IEnumerable<IBuiltinToolProvider> builtinProviders, IEnumerable<IChannelToolProvider> channelProviders, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            AgentConfig? agent = store.GetById(id);
            if (agent is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            var groups = new List<object>();

            // ── 内置分组：遍历所有 IBuiltinToolProvider，按 Agent 的 ToolGroupConfig 过滤 ──
            var groupNames = new Dictionary<string, string>
            {
                ["fetch"]    = "HTTP 抓取",
                ["shell"]    = "Shell 命令",
                ["cron"]     = "定时任务",
                ["subagent"] = "子代理 & DNA",
                ["file"]     = "文件操作",
            };
            foreach (IBuiltinToolProvider provider in builtinProviders)
            {
                string displayName = groupNames.TryGetValue(provider.GroupId, out string? n) ? n : provider.GroupId;
                ToolGroupConfig? cfg = agent.ToolGroupConfigs.FirstOrDefault(g => g.GroupId == provider.GroupId);
                bool groupEnabled = cfg is null || cfg.IsEnabled;
                groups.Add(new
                {
                    id = provider.GroupId,
                    name = displayName,
                    type = "builtin",
                    isEnabled = groupEnabled,
                    tools = provider.GetToolDescriptions().Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        isEnabled = groupEnabled && (cfg is null || !cfg.DisabledToolNames.Contains(t.Name))
                    }).ToList()
                });
            }

            // ── 渠道工具分组：遍历所有 IChannelToolProvider ──────────────────
            foreach (IChannelToolProvider channelProvider in channelProviders)
            {
                string groupId = channelProvider.ChannelType.ToString().ToLowerInvariant();
                string displayName = channelProvider.ChannelType.ToString();
                ToolGroupConfig? cfg = agent.ToolGroupConfigs.FirstOrDefault(g => g.GroupId == groupId);
                bool groupEnabled = cfg is null || cfg.IsEnabled;
                groups.Add(new
                {
                    id = groupId,
                    name = displayName,
                    type = "builtin",
                    isEnabled = groupEnabled,
                    tools = channelProvider.GetToolDescriptions().Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        isEnabled = groupEnabled && (cfg is null || !cfg.DisabledToolNames.Contains(t.Name))
                    }).ToList()
                });
            }

            // ── 全局 MCP Server 分组（EnabledMcpServerIds 为空时默认全部启用）──────────────────────────────
            HashSet<string> enabledIds = agent.EnabledMcpServerIds.ToHashSet();
            bool defaultAllEnabled = enabledIds.Count == 0;
            foreach (McpServerConfig srv in mcpStore.All)
            {
                bool isEnabled = defaultAllEnabled || enabledIds.Contains(srv.Id);
                // 若 MCP Server 本身被全局禁用，则不启用
                if (!srv.IsEnabled) isEnabled = false;
                ToolGroupConfig? srvCfg = agent.ToolGroupConfigs.FirstOrDefault(g => g.GroupId == srv.Name);
                bool groupEnabled = isEnabled && (srvCfg is null || srvCfg.IsEnabled);

                if (!groupEnabled)
                {
                    groups.Add(new
                    {
                        id = srv.Id,
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
                        id = srv.Id,
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

        // ── Agent DNA 文件管理（SOUL.md / MEMORY.md）──────────────────────────

        endpoints.MapGet("/agents/{id}/dna", (string id, AgentStore store, AgentDnaService agentDna) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            IReadOnlyList<AgentDnaFileInfo> files = agentDna.ListFiles(id);
            return Results.Ok(files.Select(f => new { f.FileName, f.Description, f.UpdatedAt }));
        })
        .WithTags("Agents");

        endpoints.MapGet("/agents/{id}/dna/{fileName}", (string id, string fileName, AgentStore store, AgentDnaService agentDna) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            AgentDnaFileInfo? file = agentDna.Read(id, fileName);
            if (file is null)
                return Results.BadRequest(new { success = false, message = $"不允许的文件名: {fileName}。仅支持 SOUL.md 和 MEMORY.md。", errorCode = "BAD_REQUEST" });

            return Results.Ok(new { file.FileName, file.Content, file.UpdatedAt });
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/{id}/dna", (string id, AgentDnaUpdateRequest req, AgentStore store, AgentDnaService agentDna) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { success = false, message = "fileName is required.", errorCode = "BAD_REQUEST" });

            AgentDnaFileInfo? result = agentDna.Update(id, req.FileName, req.Content ?? string.Empty);
            if (result is null)
                return Results.BadRequest(new { success = false, message = $"不允许的文件名: {req.FileName}。仅支持 SOUL.md 和 MEMORY.md。", errorCode = "BAD_REQUEST" });

            return Results.Ok(new { success = true });
        })
        .WithTags("Agents");

        return endpoints;
    }

    private static async Task WriteSseAsync(HttpResponse response, string data, CancellationToken ct)
    {
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static object ToDto(AgentConfig a) => new
    {
        a.Id,
        a.Name,
        a.Description,
        a.IsEnabled,
        a.BoundSkillIds,
        a.EnabledMcpServerIds,
        a.ToolGroupConfigs,
        a.CreatedAtUtc,
        a.IsDefault,
        a.ContextWindowMessages,
        a.ExposeAsA2A,
    };
}

// ── Request records ──────────────────────────────────────────────────────────

public sealed record AgentCreateRequest(
    string Name,
    string? Description = null,
    bool IsEnabled = true,
    IReadOnlyList<string>? BoundSkillIds = null,
    IReadOnlyList<string>? EnabledMcpServerIds = null,
    int? ContextWindowMessages = null,
    bool ExposeAsA2A = false);

public sealed record AgentUpdateRequest(
    string Id,
    string? Name = null,
    string? Description = null,
    bool? IsEnabled = null,
    IReadOnlyList<string>? BoundSkillIds = null,
    IReadOnlyList<string>? EnabledMcpServerIds = null,
    int? ContextWindowMessages = null,
    bool? ExposeAsA2A = null);

public sealed record AgentMcpServersRequest(IReadOnlyList<string>? McpServerIds);

public sealed record AgentBoundSkillsRequest(IReadOnlyList<string>? SkillIds);

public sealed record AgentDeleteRequest(string Id);

public sealed record AgentDnaUpdateRequest(string FileName, string? Content);

public sealed record ToolGroupConfigRequest(
    string GroupId,
    bool IsEnabled,
    IReadOnlyList<string>? DisabledToolNames = null);
