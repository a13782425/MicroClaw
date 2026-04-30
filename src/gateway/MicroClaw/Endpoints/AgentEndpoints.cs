using System.Text.Json;
using MicroClaw.Agent;
using MicroClaw.Agent.Memory;
using MicroClaw.Providers;
using MicroClaw.Skills;
using MicroClaw.Tools;

namespace MicroClaw.Endpoints;

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

        endpoints.MapGet("/agents", (MicroAgentService agentService) =>
            Results.Ok(agentService.GetAllAgentEntities().Select(ToDto)))
            .WithTags("Agents");

        endpoints.MapGet("/agents/{id}", (string id, MicroAgentService agentService) =>
        {
            AgentEntity? agent = agentService.GetAgentEntityById(id);
            return agent is null ? Results.NotFound() : Results.Ok(ToDto(agent));
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents", async (AgentCreateRequest req, MicroAgentService agentService, AgentDnaService agentDna, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { success = false, message = "Name is required.", errorCode = "BAD_REQUEST" });

            AgentEntity newAgent = AgentEntity.New(
                name: req.Name.Trim(),
                description: req.Description ?? string.Empty,
                isEnabled: req.IsEnabled,
                contextWindowMessages: req.ContextWindowMessages);
            if (req.DisabledSkillIds is not null)
                newAgent.UpdateDisabledSkillIds(req.DisabledSkillIds);
            if (req.DisabledMcpServerIds is not null)
                newAgent.UpdateDisabledMcpServerIds(req.DisabledMcpServerIds);
            if (req.AllowedSubAgentIds is not null)
                newAgent.UpdateAllowedSubAgentIds(req.AllowedSubAgentIds);
            if (req.MonthlyBudgetUsd.HasValue)
                newAgent.UpdateMonthlyBudget(req.MonthlyBudgetUsd);
            try
            {
                AgentEntity created = await agentService.CreateAgentAsync(newAgent, ct);
                try
                {
                    agentDna.InitializeAgent(created.Id);
                }
                catch
                {
                    try
                    {
                        await agentService.DeleteAgentAsync(created.Id, CancellationToken.None);
                    }
                    catch
                    {
                    }

                    try
                    {
                        agentDna.DeleteAgentFiles(created.Id);
                    }
                    catch
                    {
                    }

                    throw;
                }

                return Results.Ok(new { created.Id });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message, errorCode = "AGENT_NAME_CONFLICT" });
            }
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/update", async (AgentUpdateRequest req, MicroAgentService agentService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });
            if (req.Name is not null && string.IsNullOrWhiteSpace(req.Name))
                return Results.BadRequest(new { success = false, message = "Name cannot be empty.", errorCode = "BAD_REQUEST" });
            if (req.RoutingStrategy is not null &&
                !Enum.TryParse<ProviderRoutingStrategy>(req.RoutingStrategy, ignoreCase: true, out _))
                return Results.BadRequest(new { success = false, message = $"Unknown routing strategy '{req.RoutingStrategy}'.", errorCode = "BAD_REQUEST" });

            try
            {
                AgentEntity saved = await agentService.UpdateAgentAsync(req.Id, existing =>
                {
                    if (req.Name is not null) existing.UpdateInfo(req.Name.Trim(), req.Description ?? existing.Description);
                    else if (req.Description is not null) existing.UpdateInfo(existing.Name, req.Description);
                    if (req.IsEnabled is true) existing.Enable();
                    else if (req.IsEnabled is false) existing.Disable();
                    if (req.DisabledSkillIds is not null) existing.UpdateDisabledSkillIds(req.DisabledSkillIds);
                    if (req.DisabledMcpServerIds is not null) existing.UpdateDisabledMcpServerIds(req.DisabledMcpServerIds);
                    if (req.HasContextWindowMessages) existing.UpdateContextWindow(req.ContextWindowMessages);
                    if (req.HasAllowedSubAgentIds) existing.UpdateAllowedSubAgentIds(req.AllowedSubAgentIds);
                    if (req.RoutingStrategy is not null &&
                        Enum.TryParse<ProviderRoutingStrategy>(req.RoutingStrategy, ignoreCase: true, out var parsedStrategy))
                        existing.UpdateRoutingStrategy(parsedStrategy);
                    if (req.HasMonthlyBudgetUsd) existing.UpdateMonthlyBudget(req.MonthlyBudgetUsd);
                }, ct);
                return Results.Ok(new { saved.Id });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { success = false, message = $"Agent '{req.Id}' not found.", errorCode = "NOT_FOUND" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message, errorCode = "AGENT_NAME_CONFLICT" });
            }
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/delete", async (AgentDeleteRequest req, MicroAgentService agentService, AgentDnaService agentDna, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });

            try
            {
                await agentService.DeleteAgentAsync(req.Id, ct);
                try
                {
                    agentDna.DeleteAgentFiles(req.Id);
                }
                catch
                {
                }

                return Results.Ok();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { success = false, message = $"Agent '{req.Id}' not found.", errorCode = "NOT_FOUND" });
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(new { success = false, message = ex.Message, errorCode = "BAD_REQUEST" });
            }
        })
        .WithTags("Agents");

        // ── 全局 MCP Server 引用管理 ────────────────────────────────────────────────────────────────────────────

        endpoints.MapGet("/agents/{id}/mcp-servers", (string id, MicroAgentService agentService) =>
        {
            AgentEntity? agent = agentService.GetAgentEntityById(id);
            return agent is null
                ? Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" })
                : Results.Ok(agent.DisabledMcpServerIds);
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/{id}/mcp-servers", async (string id, AgentMcpServersRequest req, MicroAgentService agentService, McpServerConfigStore mcpStore, CancellationToken ct) =>
        {
            IReadOnlyList<string> mcpIds = req.McpServerIds ?? [];
            List<string> invalidIds = mcpIds.Where(mid => mcpStore.GetById(mid) is null).ToList();
            if (invalidIds.Count > 0)
                return Results.BadRequest(new
                {
                    success = false,
                    message = $"MCP Server(s) not found: {string.Join(", ", invalidIds)}",
                    errorCode = "MCP_SERVER_NOT_FOUND"
                });

            try
            {
                AgentEntity saved = await agentService.UpdateAgentAsync(id, existing => existing.UpdateDisabledMcpServerIds(mcpIds), ct);
                return Results.Ok(new { saved.Id });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });
            }
        })
        .WithTags("Agents");

        // ── 工具列表（内置分组 + 渠道 + MCP 分组，含启用状态）────────────

        endpoints.MapGet("/agents/{id}/tools", async (string id, MicroAgentService agentService, ToolCollector toolCollector, CancellationToken ct) =>
        {
            AgentEntity? agent = agentService.GetAgentEntityById(id);
            if (agent is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            IReadOnlyList<ToolGroupInfo> groups = await toolCollector.GetToolGroupsAsync(agent, ct);
            return Results.Ok(new { groups });
        })
        .WithTags("Agents");

        // ── 更新工具分组启用配置 ─────────────────────────────────────────────

        endpoints.MapPost("/agents/{id}/tools/settings", async (string id, IReadOnlyList<ToolGroupConfigRequest> req, MicroAgentService agentService, CancellationToken ct) =>
        {
            IReadOnlyList<ToolGroupConfig> configs = req
                .Select(r => new ToolGroupConfig(r.GroupId, r.IsEnabled, r.DisabledToolNames ?? []))
                .ToList()
                .AsReadOnly();

            try
            {
                AgentEntity saved = await agentService.UpdateAgentAsync(id, agent => agent.UpdateToolGroupConfigs(configs), ct);
                return Results.Ok(new { saved.Id });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });
            }
        })
        .WithTags("Agents");

        // ── 技能绑定管理─────────────────────────────────────────────────────

        endpoints.MapGet("/agents/{id}/skills", (string id, MicroAgentService agentService) =>
        {
            AgentEntity? agent = agentService.GetAgentEntityById(id);
            return agent is null ? Results.NotFound() : Results.Ok(agent.DisabledSkillIds);
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/{id}/skills", async (string id, AgentBoundSkillsRequest req, MicroAgentService agentService, SkillStore skillStore, CancellationToken ct) =>
        {
            IReadOnlyList<string> skillIds = req.SkillIds ?? [];
            List<string> invalidIds = skillIds.Where(sid => !skillStore.Exists(sid)).ToList();
            if (invalidIds.Count > 0)
                return Results.BadRequest(new
                {
                    success = false,
                    message = $"Skill(s) not found: {string.Join(", ", invalidIds)}",
                    errorCode = "SKILL_NOT_FOUND"
                });

            try
            {
                AgentEntity saved = await agentService.UpdateAgentAsync(id, existing => existing.UpdateDisabledSkillIds(skillIds), ct);
                return Results.Ok(new { saved.Id });
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });
            }
        })
        .WithTags("Agents");


        // ── Agent DNA 文件管理（SOUL.md / MEMORY.md）──────────────────────────

        // ── 子代里查询 ──────────────────────────────────────────────────

        endpoints.MapGet("/agents/{id}/sub-agents", (string id, MicroAgentService agentService) =>
        {
            AgentEntity? agent = agentService.GetAgentEntityById(id);
            if (agent is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            // 根据 ACL 过滤可调用子代理列表（排除自身），使用 Agent.CanCallSubAgent 行为方法
            IEnumerable<AgentEntity> candidates = agentService.GetAllAgentEntities().Where(a => a.IsEnabled && a.Id != id && agent.CanCallSubAgent(a.Id));

            var result = candidates.Select(a => new { a.Id, a.Name, a.Description }).ToList();
            return Results.Ok(result);
        })
        .WithTags("Agents");

        endpoints.MapGet("/agents/{id}/dna", (string id, MicroAgentService agentService, AgentDnaService agentDna) =>
        {
            if (agentService.GetAgentEntityById(id) is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            IReadOnlyList<AgentDnaFileInfo> files = agentDna.ListFiles(id);
            return Results.Ok(files.Select(f => new { f.FileName, f.Description, f.Content, f.UpdatedAt }));
        })
        .WithTags("Agents");

        endpoints.MapGet("/agents/{id}/dna/{fileName}", (string id, string fileName, MicroAgentService agentService, AgentDnaService agentDna) =>
        {
            if (agentService.GetAgentEntityById(id) is null)
                return Results.NotFound(new { success = false, message = $"Agent '{id}' not found.", errorCode = "NOT_FOUND" });

            AgentDnaFileInfo? file = agentDna.Read(id, fileName);
            if (file is null)
                return Results.BadRequest(new { success = false, message = $"不允许的文件名: {fileName}。仅支持 SOUL.md 和 MEMORY.md。", errorCode = "BAD_REQUEST" });

            return Results.Ok(new { file.FileName, file.Content, file.UpdatedAt });
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/{id}/dna", (string id, AgentDnaUpdateRequest req, MicroAgentService agentService, AgentDnaService agentDna) =>
        {
            if (agentService.GetAgentEntityById(id) is null)
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

    private static object ToDto(AgentEntity a) => new
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
    bool HasContextWindowMessages = false,

    IReadOnlyList<string>? AllowedSubAgentIds = null,

    bool HasAllowedSubAgentIds = false,

    string? RoutingStrategy = null,
    decimal? MonthlyBudgetUsd = null,
    bool HasMonthlyBudgetUsd = false);

public sealed record AgentMcpServersRequest(IReadOnlyList<string>? McpServerIds);

public sealed record AgentBoundSkillsRequest(IReadOnlyList<string>? SkillIds);

public sealed record AgentDeleteRequest(string Id);

public sealed record AgentDnaUpdateRequest(string FileName, string? Content);

public sealed record ToolGroupConfigRequest(
    string GroupId,
    bool IsEnabled,
    IReadOnlyList<string>? DisabledToolNames = null);


