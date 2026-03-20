using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MicroClaw.Agent;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Hubs;
using MicroClaw.Providers;
using MicroClaw.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace MicroClaw.Endpoints;

public static class SessionEndpoints
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // GET /api/sessions — 获取所有会话
        endpoints.MapGet("/sessions", (SessionStore store) =>
            Results.Ok(store.All))
            .WithTags("Sessions");

        // POST /api/sessions — 创建会话
        endpoints.MapPost("/sessions", (CreateSessionRequest req, SessionStore store, ProviderConfigStore providerStore) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title))
                return Results.BadRequest(new { message = "Title is required." });
            if (string.IsNullOrWhiteSpace(req.ProviderId))
                return Results.BadRequest(new { message = "ProviderId is required." });

            ProviderConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == req.ProviderId);
            if (provider is null)
                return Results.NotFound(new { message = $"Provider '{req.ProviderId}' not found." });

            SessionInfo created = store.Create(req.Title.Trim(), req.ProviderId, ChannelType.Web);
            return Results.Ok(created);
        })
        .WithTags("Sessions");

        // POST /api/sessions/delete — 删除会话
        endpoints.MapPost("/sessions/delete", (DeleteSessionRequest req, SessionStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { message = "Id is required." });

            bool deleted = store.Delete(req.Id);
            return deleted ? Results.Ok() : Results.NotFound(new { message = $"Session '{req.Id}' not found." });
        })
        .WithTags("Sessions");

        // POST /api/sessions/approve — 审批会话（仅 admin）
        endpoints.MapPost("/sessions/approve", async (ApproveSessionRequest req, SessionStore store, ClaimsPrincipal user, IHubContext<GatewayHub> hub) =>
        {
            if (!user.IsInRole("admin"))
                return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { message = "Id is required." });

            SessionInfo? updated = store.Approve(req.Id);
            if (updated is null)
                return Results.NotFound(new { message = $"Session '{req.Id}' not found." });

            await hub.Clients.All.SendAsync("sessionApproved", new { sessionId = updated.Id, title = updated.Title });
            return Results.Ok(updated);
        })
        .WithTags("Sessions");

        // POST /api/sessions/disable — 禁用会话（仅 admin）
        endpoints.MapPost("/sessions/disable", async (DisableSessionRequest req, SessionStore store, ClaimsPrincipal user, IHubContext<GatewayHub> hub) =>
        {
            if (!user.IsInRole("admin"))
                return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { message = "Id is required." });

            SessionInfo? updated = store.Disable(req.Id);
            if (updated is null)
                return Results.NotFound(new { message = $"Session '{req.Id}' not found." });

            await hub.Clients.All.SendAsync("sessionDisabled", new { sessionId = updated.Id, title = updated.Title });
            return Results.Ok(updated);
        })
        .WithTags("Sessions");

        // POST /api/sessions/switch-provider — 切换会话绑定的 Provider
        endpoints.MapPost("/sessions/switch-provider", (SwitchProviderRequest req, SessionStore store, ProviderConfigStore providerStore) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { message = "Id is required." });
            if (string.IsNullOrWhiteSpace(req.ProviderId))
                return Results.BadRequest(new { message = "ProviderId is required." });

            ProviderConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == req.ProviderId);
            if (provider is null || !provider.IsEnabled)
                return Results.NotFound(new { message = $"Provider '{req.ProviderId}' not found or disabled." });

            SessionInfo? updated = store.UpdateProvider(req.Id, req.ProviderId);
            return updated is null
                ? Results.NotFound(new { message = $"Session '{req.Id}' not found." })
                : Results.Ok(updated);
        })
        .WithTags("Sessions");

        // GET /api/sessions/{id}/messages — 获取消息历史
        endpoints.MapGet("/sessions/{id}/messages", (string id, SessionStore store) =>
        {
            SessionInfo? session = store.Get(id);
            if (session is null)
                return Results.NotFound(new { message = $"Session '{id}' not found." });

            IReadOnlyList<SessionMessage> messages = store.GetMessages(id);
            return Results.Ok(messages);
        })
        .WithTags("Sessions");

        // POST /api/sessions/{id}/chat — SSE 流式对话
        endpoints.MapPost("/sessions/{id}/chat",
            async (string id, ChatRequest req, SessionStore store,
                   ProviderConfigStore providerStore,
                   AgentStore agentStore, AgentRunner agentRunner,
                   HttpContext ctx, CancellationToken ct) =>
            {
                SessionInfo? session = store.Get(id);
                if (session is null)
                {
                    ctx.Response.StatusCode = 404;
                    await ctx.Response.WriteAsJsonAsync(new { message = $"Session '{id}' not found." }, ct);
                    return;
                }

                if (!session.IsApproved)
                {
                    ctx.Response.StatusCode = 403;
                    await ctx.Response.WriteAsJsonAsync(new { message = "会话尚未获得批准，请联系管理员。" }, ct);
                    return;
                }

                ProviderConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == session.ProviderId);
                if (provider is null)
                {
                    ctx.Response.StatusCode = 400;
                    await ctx.Response.WriteAsJsonAsync(new { message = $"Provider '{session.ProviderId}' not found." }, ct);
                    return;
                }

                // 保存用户消息
                SessionMessage userMessage = new(
                    Role: "user",
                    Content: req.Content,
                    ThinkContent: null,
                    Timestamp: DateTimeOffset.UtcNow,
                    Attachments: req.Attachments);
                store.AddMessage(id, userMessage);

                // 构建历史消息上下文
                IReadOnlyList<SessionMessage> history = store.GetMessages(id);

                // 获取默认 Agent（技能、MCP、DNA 等能力均通过 AgentRunner 注入）
                AgentConfig? defaultAgent = agentStore.GetDefault();
                if (defaultAgent is null || !defaultAgent.IsEnabled)
                {
                    ctx.Response.StatusCode = 503;
                    await ctx.Response.WriteAsJsonAsync(new { message = "No enabled default agent found." }, ct);
                    return;
                }

                // 设置 SSE 响应头
                ctx.Response.ContentType = "text/event-stream; charset=utf-8";
                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.Headers.Connection = "keep-alive";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";

                StringBuilder fullContent = new();

                try
                {
                    await foreach (string token in
                        agentRunner.StreamReActAsync(defaultAgent, session.ProviderId, history, id, ct))
                    {
                        fullContent.Append(token);

                        string sseData = JsonSerializer.Serialize(new { type = "token", content = token }, JsonOpts);
                        await WriteSseAsync(ctx.Response, sseData, ct);
                    }

                    // 解析 think 块
                    (string Think, string Main) parsed = ExtractThinkContent(fullContent.ToString());

                    // 保存助手消息（带 think 内容）
                    SessionMessage assistantMessage = new(
                        Role: "assistant",
                        Content: parsed.Main,
                        ThinkContent: string.IsNullOrWhiteSpace(parsed.Think) ? null : parsed.Think,
                        Timestamp: DateTimeOffset.UtcNow,
                        Attachments: null);
                    store.AddMessage(id, assistantMessage);

                    // 发送完成信号
                    string doneData = JsonSerializer.Serialize(new
                    {
                        type = "done",
                        thinkContent = assistantMessage.ThinkContent
                    }, JsonOpts);
                    await WriteSseAsync(ctx.Response, doneData, ct);
                    await ctx.Response.WriteAsync("data: [DONE]\n\n", ct);
                }
                catch (OperationCanceledException)
                {
                    // 客户端断开，静默处理
                }
                catch (Exception ex)
                {
                    try
                    {
                        string errData = JsonSerializer.Serialize(new { type = "error", message = ex.Message }, JsonOpts);
                        await WriteSseAsync(ctx.Response, errData, CancellationToken.None);
                        await ctx.Response.WriteAsync("data: [DONE]\n\n", CancellationToken.None);
                        await ctx.Response.Body.FlushAsync(CancellationToken.None);
                    }
                    catch
                    {
                        // 响应已关闭，忽略
                    }
                }

            })
        .WithTags("Sessions");

        return endpoints;
    }

    private static (string Think, string Main) ExtractThinkContent(string raw)
    {
        string think = string.Empty;
        string main = raw;

        int start = raw.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
        int end = raw.IndexOf("</think>", StringComparison.OrdinalIgnoreCase);

        if (start >= 0 && end > start)
        {
            think = raw[(start + 7)..end].Trim();
            main = (raw[..start] + raw[(end + 8)..]).Trim();
        }

        return (think, main);
    }

    private static async Task WriteSseAsync(HttpResponse response, string data, CancellationToken ct)
    {
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
