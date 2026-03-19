using System.Text;
using System.Text.Json;
using MicroClaw.Agent.Memory;
using MicroClaw.Agent.Tools;
using MicroClaw.Gateway.Contracts.Sessions;
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
                return Results.BadRequest(new { message = "Name is required." });
            if (string.IsNullOrWhiteSpace(req.ProviderId))
                return Results.BadRequest(new { message = "ProviderId is required." });

            AgentConfig config = new(
                Id: string.Empty,
                Name: req.Name.Trim(),
                SystemPrompt: req.SystemPrompt ?? string.Empty,
                ProviderId: req.ProviderId.Trim(),
                IsEnabled: req.IsEnabled,
                BoundChannelIds: req.BoundChannelIds ?? [],
                McpServers: req.McpServers ?? [],
                CreatedAtUtc: DateTimeOffset.UtcNow);

            AgentConfig created = store.Add(config);
            return Results.Ok(new { created.Id });
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/update", (AgentUpdateRequest req, AgentStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { message = "Id is required." });

            AgentConfig? existing = store.GetById(req.Id);
            if (existing is null)
                return Results.NotFound(new { message = $"Agent '{req.Id}' not found." });

            AgentConfig updated = existing with
            {
                Name = req.Name?.Trim() ?? existing.Name,
                SystemPrompt = req.SystemPrompt ?? existing.SystemPrompt,
                ProviderId = req.ProviderId?.Trim() ?? existing.ProviderId,
                IsEnabled = req.IsEnabled ?? existing.IsEnabled,
                BoundChannelIds = req.BoundChannelIds ?? existing.BoundChannelIds,
                McpServers = req.McpServers ?? existing.McpServers,
            };

            AgentConfig? result = store.Update(req.Id, updated);
            return result is null ? Results.NotFound() : Results.Ok(new { result.Id });
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/delete", (AgentDeleteRequest req, AgentStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { message = "Id is required." });

            bool deleted = store.Delete(req.Id);
            return deleted ? Results.Ok() : Results.NotFound(new { message = $"Agent '{req.Id}' not found." });
        })
        .WithTags("Agents");

        // ── DNA 基因文件管理 ─────────────────────────────────────────────────

        endpoints.MapGet("/agents/{id}/dna", (string id, AgentStore store, DNAService dna) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { message = $"Agent '{id}' not found." });

            return Results.Ok(dna.List(id));
        })
        .WithTags("Agents");

        endpoints.MapPost("/agents/{id}/dna", (string id, GeneFileWriteRequest req, AgentStore store, DNAService dna) =>
        {
            if (store.GetById(id) is null)
                return Results.NotFound(new { message = $"Agent '{id}' not found." });
            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { message = "FileName is required." });

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

        // ── MCP 工具列表 ─────────────────────────────────────────────────────

        endpoints.MapGet("/agents/{id}/tools", async (string id, AgentStore store, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            AgentConfig? agent = store.GetById(id);
            if (agent is null)
                return Results.NotFound(new { message = $"Agent '{id}' not found." });

            var (tools, connections) = await ToolRegistry.LoadToolsAsync(agent.McpServers, loggerFactory, ct);
            try
            {
                var toolList = tools.Select(t => new { t.Name, t.Description }).ToList();
                return Results.Ok(toolList);
            }
            finally
            {
                foreach (IAsyncDisposable conn in connections)
                    try { await conn.DisposeAsync(); } catch { }
            }
        })
        .WithTags("Agents");

        // ── Agent 流式对话（SSE）───────────────────────────────────────────

        endpoints.MapPost("/agents/{id}/chat",
            async (string id, AgentChatRequest req, AgentStore store, AgentRunner runner,
                   HttpContext ctx, CancellationToken ct) =>
            {
                AgentConfig? agent = store.GetById(id);
                if (agent is null)
                {
                    ctx.Response.StatusCode = 404;
                    await ctx.Response.WriteAsJsonAsync(new { message = $"Agent '{id}' not found." }, ct);
                    return;
                }

                if (string.IsNullOrWhiteSpace(req.Content))
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsJsonAsync(new { message = "Content is required." }, ct);
                    return;
                }

                List<SessionMessage> history = (req.History ?? [])
                    .Append(new SessionMessage("user", req.Content, null, DateTimeOffset.UtcNow, null))
                    .ToList();

                ctx.Response.ContentType = "text/event-stream; charset=utf-8";
                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.Headers.Connection = "keep-alive";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";

                try
                {
                    var fullContent = new StringBuilder();
                    await foreach (string token in runner.StreamReActAsync(agent, history, sessionId: null, ct))
                    {
                        fullContent.Append(token);
                        string sseData = JsonSerializer.Serialize(new { type = "token", content = token }, JsonOpts);
                        await WriteSseAsync(ctx.Response, sseData, ct);
                    }

                    string doneData = JsonSerializer.Serialize(new { type = "done" }, JsonOpts);
                    await WriteSseAsync(ctx.Response, doneData, ct);
                    await ctx.Response.WriteAsync("data: [DONE]\n\n", ct);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    try
                    {
                        string errData = JsonSerializer.Serialize(new { type = "error", message = ex.Message }, JsonOpts);
                        await WriteSseAsync(ctx.Response, errData, CancellationToken.None);
                        await ctx.Response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
                        await ctx.Response.Body.FlushAsync(CancellationToken.None);
                    }
                    catch { }
                }
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
        a.ProviderId,
        a.IsEnabled,
        a.BoundChannelIds,
        a.McpServers,
        a.CreatedAtUtc,
    };
}

// ── Request records ──────────────────────────────────────────────────────────

public sealed record AgentCreateRequest(
    string Name,
    string? SystemPrompt,
    string ProviderId,
    bool IsEnabled = true,
    IReadOnlyList<string>? BoundChannelIds = null,
    IReadOnlyList<McpServerConfig>? McpServers = null);

public sealed record AgentUpdateRequest(
    string Id,
    string? Name = null,
    string? SystemPrompt = null,
    string? ProviderId = null,
    bool? IsEnabled = null,
    IReadOnlyList<string>? BoundChannelIds = null,
    IReadOnlyList<McpServerConfig>? McpServers = null);

public sealed record AgentDeleteRequest(string Id);

public sealed record GeneFileWriteRequest(
    string FileName,
    string? Category,
    string? Content);

public sealed record GeneFileDeleteRequest(
    string FileName,
    string? Category);

public sealed record AgentChatRequest(
    string Content,
    IReadOnlyList<SessionMessage>? History = null);
