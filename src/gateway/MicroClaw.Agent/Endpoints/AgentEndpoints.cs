using System.Text.Json;
using MicroClaw.Agent.Memory;
using MicroClaw.Providers;
using MicroClaw.Skills;
using MicroClaw.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MicroClaw.Agent.Endpoints;

/// <summary>
/// Agent REST API 端点：Agent CRUD、MCP Server 引用管理、工具列表、流式对话�?
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

        endpoints.MapGet("/agents", (IAgentRepository agentRepo) =>
            Results.Ok(agentRepo.GetAll().Select(ToDto)))
            .WithTags("Agents");

        endpoints.MapGet("/agents/{id}", (string id, AgentStore store) =>
        {
            Agent? agent = store.GetAgentById(id);
            return agent is null ? Results.NotFound() : Results.Ok(ToDto(agent));
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents", (AgentCreateRequest req, AgentStore store, AgentDnaService agentDna) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { success = false, message = "Name is required.", errorCode = "BAD_REQUEST" });

            Agent newAgent = Agent.Create(
                name: req.Name.Trim(),
                description: req.Description ?? string.Empty,
                isEnabled: req.IsEnabled,
                disabledSkillIds: req.DisabledSkillIds ?? [],
                disabledMcpServerIds: req.DisabledMcpServerIds ?? [],
                contextWindowMessages: req.ContextWindowMessages,
                exposeAsA2A: req.ExposeAsA2A,
                allowedSubAgentIds: req.AllowedSubAgentIds);
            try
            {
                Agent created = ((IAgentRepository)store).Save(newAgent);
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

            Agent? existing = store.GetAgentById(req.Id);
            if (existing is null)
                return Results.NotFound(new { success = false, message = $"Agent '{req.Id}' not found.", errorCode = "NOT_FOUND" });

            // 使用 Agent 行为方法应用更新
            if (req.Name is not null) existing.UpdateInfo(req.Name.Trim(), req.Description ?? existing.Description);
            else if (req.Description is not null) existing.UpdateInfo(existing.Name, req.Description);
            if (req.IsEnabled is true) existing.Enable();
            else if (req.IsEnabled is false) existing.Disable();
            if (req.DisabledSkillIds is not null) existing.UpdateDisabledSkillIds(req.DisabledSkillIds);
            if (req.DisabledMcpServerIds is not null) existing.UpdateDisabledMcpServerIds(req.DisabledMcpServerIds);
            if (req.HasContextWindowMessages) existing.UpdateContextWindow(req.ContextWindowMessages);
            if (req.ExposeAsA2A is not null) existing.UpdateExposeAsA2A(req.ExposeAsA2A.Value);
            if (req.HasAllowedSubAgentIds) existing.UpdateAllowedSubAgentIds(req.AllowedSubAgentIds);
            if (req.RoutingStrategy is not null &&
                Enum.TryParse<ProviderRoutingStrategy>(req.RoutingStrategy, ignoreCase: true, out var parsedStrategy))
                existing.UpdateRoutingStrategy(parsedStrategy);
            if (req.HasMonthlyBudgetUsd) existing.UpdateMonthlyBudget(req.MonthlyBudgetUsd);

            try
            {
                Agent saved = ((IAgentRepository)store).Save(existing);
                return Results.Ok(new { saved.Id });
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

            Agent? agent = store.GetAgentById(req.Id);
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
            Agent? agent = store.GetAgentById(id);
            return agent is null
                ? Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" })
                : Results.Ok(agent.DisabledMcpServerIds);
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/{id}/mcp-servers", (string id, AgentMcpServersRequest req, AgentStore store, McpServerConfigStore mcpStore) =>
        {
            Agent? existing = store.GetAgentById(id);
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

            existing.UpdateDisabledMcpServerIds(mcpIds);
            ((IAgentRepository)store).Save(existing);
            return Results.Ok(new { existing.Id });
        })
        .WithTags("Agents");

        // ── 工具列表（内置分组 + 渠道 + MCP 分组，含启用状态）────────────

        endpoints.MapGet("/agents/{id}/tools", async (string id, AgentStore store, ToolCollector toolCollector, CancellationToken ct) =>
        {
            Agent? agent = store.GetAgentById(id);
            if (agent is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            IReadOnlyList<ToolGroupInfo> groups = await toolCollector.GetToolGroupsAsync(agent, ct);
            return Results.Ok(new { groups });
        })
        .WithTags("Agents");

        // ── 更新工具分组启用配置 ─────────────────────────────────────────────

        endpoints.MapPost("/agents/{id}/tools/settings", (string id, IReadOnlyList<ToolGroupConfigRequest> req, AgentStore store) =>
        {
            Agent? agent = store.GetAgentById(id);
            if (agent is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            IReadOnlyList<ToolGroupConfig> configs = req
                .Select(r => new ToolGroupConfig(r.GroupId, r.IsEnabled, r.DisabledToolNames ?? []))
                .ToList()
                .AsReadOnly();

            agent.UpdateToolGroupConfigs(configs);
            ((IAgentRepository)store).Save(agent);
            return Results.Ok(new { agent.Id });
        })
        .WithTags("Agents");

        // ── 技能绑定管�?─────────────────────────────────────────────────────

        endpoints.MapGet("/agents/{id}/skills", (string id, AgentStore store) =>
        {
            Agent? agent = store.GetAgentById(id);
            return agent is null ? Results.NotFound() : Results.Ok(agent.DisabledSkillIds);
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/{id}/skills", (string id, AgentBoundSkillsRequest req, AgentStore store, SkillStore skillStore) =>
        {
            Agent? existing = store.GetAgentById(id);
            if (existing is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            // 0-B-6: 批量校验 SkillId 是否存在于文件系�?
            IReadOnlyList<string> skillIds = req.SkillIds ?? [];
            List<string> invalidIds = skillIds.Where(sid => !skillStore.Exists(sid)).ToList();
            if (invalidIds.Count > 0)
                return Results.BadRequest(new
                {
                    success = false,
                    message = $"Skill(s) not found: {string.Join(", ", invalidIds)}",
                    errorCode = "SKILL_NOT_FOUND"
                });

            existing.UpdateDisabledSkillIds(skillIds);
            ((IAgentRepository)store).Save(existing);
            return Results.Ok(new { existing.Id });
        })
        .WithTags("Agents");

        // ── Agent DNA 文件管理（SOUL.md / MEMORY.md）──────────────────────────

        // ── 子代�?ACL 查询 ──────────────────────────────────────────────────

        endpoints.MapGet("/agents/{id}/sub-agents", (string id, AgentStore store) =>
        {
            Agent? agent = store.GetAgentById(id);
            if (agent is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            // 根据 ACL 过滤可调用子代理列表（排除自身），使用 Agent.CanCallSubAgent 行为方法
            IEnumerable<AgentConfig> candidates = store.All.Where(a => a.IsEnabled && a.Id != id && agent.CanCallSubAgent(a.Id));

            var result = candidates.Select(a => new { a.Id, a.Name, a.Description }).ToList();
            return Results.Ok(result);
        })
        .WithTags("Agents");

        endpoints.MapGet("/agents/{id}/dna", (string id, AgentStore store, AgentDnaService agentDna) =>
        {
            if (store.GetAgentById(id) is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            IReadOnlyList<AgentDnaFileInfo> files = agentDna.ListFiles(id);
            return Results.Ok(files.Select(f => new { f.FileName, f.Description, f.Content, f.UpdatedAt }));
        })
        .WithTags("Agents");

        endpoints.MapGet("/agents/{id}/dna/{fileName}", (string id, string fileName, AgentStore store, AgentDnaService agentDna) =>
        {
            if (store.GetAgentById(id) is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            AgentDnaFileInfo? file = agentDna.Read(id, fileName);
            if (file is null)
                return Results.BadRequest(new { success = false, message = $"不允许的文件名: {fileName}。仅支持 SOUL.md 和 MEMORY.md。", errorCode = "BAD_REQUEST" });

            return Results.Ok(new { file.FileName, file.Content, file.UpdatedAt });
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/{id}/dna", (string id, AgentDnaUpdateRequest req, AgentStore store, AgentDnaService agentDna) =>
        {
            if (store.GetAgentById(id) is null)
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

    private static object ToDto(Agent a) => new
    {
        a.Id,
        a.Name,
        a.Description,
        a.IsEnabled,
        a.DisabledSkillIds,
        a.DisabledMcpServerIds,
        a.ToolGroupConfigs,
        a.CreatedAtUtc,
        a.IsDefault,
        a.ContextWindowMessages,
        a.ExposeAsA2A,
        a.AllowedSubAgentIds,
        RoutingStrategy = a.RoutingStrategy.ToString(),
        a.MonthlyBudgetUsd,
    };
}

// ── Request records ──────────────────────────────────────────────────────────

public sealed record AgentCreateRequest(
    string Name,
    string? Description = null,
    bool IsEnabled = true,
    IReadOnlyList<string>? DisabledSkillIds = null,
    IReadOnlyList<string>? DisabledMcpServerIds = null,
    int? ContextWindowMessages = null,
    bool ExposeAsA2A = false,
    IReadOnlyList<string>? AllowedSubAgentIds = null,
    decimal? MonthlyBudgetUsd = null);

public sealed record AgentUpdateRequest(
    string Id,
    string? Name = null,
    string? Description = null,
    bool? IsEnabled = null,
    IReadOnlyList<string>? DisabledSkillIds = null,
    IReadOnlyList<string>? DisabledMcpServerIds = null,
    int? ContextWindowMessages = null,
    /// <summary>是否明确传入 ContextWindowMessages（用于区�?null=未传 vs null=清除限制）�?/summary>
    bool HasContextWindowMessages = false,
    bool? ExposeAsA2A = null,
    IReadOnlyList<string>? AllowedSubAgentIds = null,
    /// <summary>
    /// 显式标记是否传入�?AllowedSubAgentIds�?
    /// 用于区分“未传”（保留原值）和“传�?null”（清除限制）�?
    /// 前端�?true + AllowedSubAgentIds = null 表示“允许所有”；�?true + [] 表示“禁止所有”�?
    /// </summary>
    bool HasAllowedSubAgentIds = false,
    /// <summary>
    /// Provider 路由策略（Default/QualityFirst/CostFirst/LatencyFirst）�?
    /// null 表示不修改，保留原有策略�?
    /// </summary>
    string? RoutingStrategy = null,
    /// <summary>月度预算上限（USD）。null 表示不修改，保留原有属性�?/summary>
    decimal? MonthlyBudgetUsd = null,
    /// <summary>是否明确传入 MonthlyBudgetUsd（用于区�?null=未传 vs null=清除预算）�?/summary>
    bool HasMonthlyBudgetUsd = false);

public sealed record AgentMcpServersRequest(IReadOnlyList<string>? McpServerIds);

public sealed record AgentBoundSkillsRequest(IReadOnlyList<string>? SkillIds);

public sealed record AgentDeleteRequest(string Id);

public sealed record AgentDnaUpdateRequest(string FileName, string? Content);

public sealed record ToolGroupConfigRequest(
    string GroupId,
    bool IsEnabled,
    IReadOnlyList<string>? DisabledToolNames = null);


