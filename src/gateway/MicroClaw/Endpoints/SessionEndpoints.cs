using System.Security.Claims;
using System.Text;
using System.Text.Json;
using MicroClaw.Agent;
using MicroClaw.Agent.Endpoints;
using MicroClaw.Agent.Memory;
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
                return Results.BadRequest(new { success = false, message = "Title is required.", errorCode = "BAD_REQUEST" });
            if (string.IsNullOrWhiteSpace(req.ProviderId))
                return Results.BadRequest(new { success = false, message = "ProviderId is required.", errorCode = "BAD_REQUEST" });

            ProviderConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == req.ProviderId);
            if (provider is null)
                return Results.NotFound(new { success = false, message = $"Provider '{req.ProviderId}' not found.", errorCode = "NOT_FOUND" });

            SessionInfo created = store.Create(req.Title.Trim(), req.ProviderId, ChannelType.Web);
            return Results.Ok(created);
        })
        .WithTags("Sessions");

        // POST /api/sessions/delete — 删除会话
        endpoints.MapPost("/sessions/delete", (DeleteSessionRequest req, SessionStore store, DNAService dna) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });

            bool deleted = store.Delete(req.Id);
            if (!deleted)
                return Results.NotFound(new { success = false, message = $"Session '{req.Id}' not found.", errorCode = "NOT_FOUND" });

            // 同步删除会话 DNA 目录
            dna.DeleteSessionDnaDir(req.Id);
            return Results.Ok();
        })
        .WithTags("Sessions");

        // POST /api/sessions/approve — 审批会话（仅 admin）
        endpoints.MapPost("/sessions/approve", async (ApproveSessionRequest req, SessionStore store, ClaimsPrincipal user, IHubContext<GatewayHub> hub) =>
        {
            if (!user.IsInRole("admin"))
                return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });

            SessionInfo? updated = store.Approve(req.Id, req.Reason);
            if (updated is null)
                return Results.NotFound(new { success = false, message = $"Session '{req.Id}' not found.", errorCode = "NOT_FOUND" });

            await hub.Clients.All.SendAsync("sessionApproved", new { sessionId = updated.Id, title = updated.Title });
            return Results.Ok(updated);
        })
        .WithTags("Sessions");

        // POST /api/sessions/approve-batch — 批量审批会话（仅 admin）
        endpoints.MapPost("/sessions/approve-batch", async (BatchApproveSessionRequest req, SessionStore store, ClaimsPrincipal user, IHubContext<GatewayHub> hub) =>
        {
            if (!user.IsInRole("admin"))
                return Results.Forbid();
            if (req.Ids is null || req.Ids.Count == 0)
                return Results.BadRequest(new { success = false, message = "Ids is required.", errorCode = "BAD_REQUEST" });

            IReadOnlyList<SessionInfo> updated = store.ApproveBatch(req.Ids, req.Reason);
            foreach (SessionInfo s in updated)
                await hub.Clients.All.SendAsync("sessionApproved", new { sessionId = s.Id, title = s.Title });
            return Results.Ok(new { updated, count = updated.Count });
        })
        .WithTags("Sessions");

        // POST /api/sessions/disable — 禁用会话（仅 admin）
        endpoints.MapPost("/sessions/disable", async (DisableSessionRequest req, SessionStore store, ClaimsPrincipal user, IHubContext<GatewayHub> hub) =>
        {
            if (!user.IsInRole("admin"))
                return Results.Forbid();
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });

            SessionInfo? updated = store.Disable(req.Id, req.Reason);
            if (updated is null)
                return Results.NotFound(new { success = false, message = $"Session '{req.Id}' not found.", errorCode = "NOT_FOUND" });

            await hub.Clients.All.SendAsync("sessionDisabled", new { sessionId = updated.Id, title = updated.Title });
            return Results.Ok(updated);
        })
        .WithTags("Sessions");

        // POST /api/sessions/disable-batch — 批量禁用会话（仅 admin）
        endpoints.MapPost("/sessions/disable-batch", async (BatchDisableSessionRequest req, SessionStore store, ClaimsPrincipal user, IHubContext<GatewayHub> hub) =>
        {
            if (!user.IsInRole("admin"))
                return Results.Forbid();
            if (req.Ids is null || req.Ids.Count == 0)
                return Results.BadRequest(new { success = false, message = "Ids is required.", errorCode = "BAD_REQUEST" });

            IReadOnlyList<SessionInfo> updated = store.DisableBatch(req.Ids, req.Reason);
            foreach (SessionInfo s in updated)
                await hub.Clients.All.SendAsync("sessionDisabled", new { sessionId = s.Id, title = s.Title });
            return Results.Ok(new { updated, count = updated.Count });
        })
        .WithTags("Sessions");

        // POST /api/sessions/switch-provider — 切换会话绑定的 Provider
        endpoints.MapPost("/sessions/switch-provider", (SwitchProviderRequest req, SessionStore store, ProviderConfigStore providerStore) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });
            if (string.IsNullOrWhiteSpace(req.ProviderId))
                return Results.BadRequest(new { success = false, message = "ProviderId is required.", errorCode = "BAD_REQUEST" });

            ProviderConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == req.ProviderId);
            if (provider is null || !provider.IsEnabled)
                return Results.NotFound(new { success = false, message = $"Provider '{req.ProviderId}' not found or disabled.", errorCode = "NOT_FOUND" });

            SessionInfo? updated = store.UpdateProvider(req.Id, req.ProviderId);
            return updated is null
                ? Results.NotFound(new { success = false, message = $"Session '{req.Id}' not found.", errorCode = "NOT_FOUND" })
                : Results.Ok(updated);
        })
        .WithTags("Sessions");

        // GET /api/sessions/{id}/messages — 获取消息历史
        // 可选分页参数：?skip=0&limit=50（skip 从末尾计数，省略时返回全量）
        endpoints.MapGet("/sessions/{id}/messages", (string id, SessionStore store, int? skip, int? limit) =>
        {
            SessionInfo? session = store.Get(id);
            if (session is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            if (limit.HasValue)
            {
                int actualSkip = Math.Max(0, skip ?? 0);
                int actualLimit = Math.Clamp(limit.Value, 1, 500);
                (IReadOnlyList<SessionMessage> messages, int total) = store.GetMessagesPaged(id, actualSkip, actualLimit);
                return Results.Ok(new { messages, total, hasMore = total > actualSkip + messages.Count });
            }

            return Results.Ok(store.GetMessages(id));
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

        // ── 会话 DNA 端点（三层架构第三层）────────────────────────────────────────────────

        // GET /api/sessions/{id}/dna — 列出会话 DNA 文件
        endpoints.MapGet("/sessions/{id}/dna", (string id, SessionStore store, DNAService dna) =>
        {
            if (store.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            return Results.Ok(dna.ListSession(id));
        })
        .WithTags("SessionDNA");

        // POST /api/sessions/{id}/dna — 写入/更新会话 DNA 文件
        endpoints.MapPost("/sessions/{id}/dna", (string id, GeneFileWriteRequest req, SessionStore store, DNAService dna) =>
        {
            if (store.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });
            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { success = false, message = "FileName is required.", errorCode = "BAD_REQUEST" });

            string safeName = Path.GetFileName(req.FileName);
            string safeCategory = SanitizeCategory(req.Category);

            GeneFile file = dna.WriteSession(id, safeCategory, safeName, req.Content ?? string.Empty);
            return Results.Ok(file);
        })
        .WithTags("SessionDNA");

        // POST /api/sessions/{id}/dna/delete — 删除会话 DNA 文件
        endpoints.MapPost("/sessions/{id}/dna/delete", (string id, GeneFileDeleteRequest req, SessionStore store, DNAService dna) =>
        {
            if (store.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            string safeName = Path.GetFileName(req.FileName ?? string.Empty);
            bool deleted = dna.DeleteSession(id, SanitizeCategory(req.Category), safeName);
            return deleted ? Results.Ok() : Results.NotFound();
        })
        .WithTags("SessionDNA");

        // GET /api/sessions/{id}/dna/snapshots — 快照列表
        endpoints.MapGet("/sessions/{id}/dna/snapshots", (string id, string fileName, string? category, SessionStore store, DNAService dna) =>
        {
            if (store.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });
            if (string.IsNullOrWhiteSpace(fileName))
                return Results.BadRequest(new { success = false, message = "fileName query parameter is required.", errorCode = "BAD_REQUEST" });

            string safeName = Path.GetFileName(fileName);
            string safeCategory = SanitizeCategory(category);
            return Results.Ok(dna.ListSessionSnapshots(id, safeCategory, safeName));
        })
        .WithTags("SessionDNA");

        // POST /api/sessions/{id}/dna/restore — 还原快照
        endpoints.MapPost("/sessions/{id}/dna/restore", (string id, GeneFileRestoreRequest req, SessionStore store, DNAService dna) =>
        {
            if (store.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });
            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { success = false, message = "FileName is required.", errorCode = "BAD_REQUEST" });
            if (string.IsNullOrWhiteSpace(req.SnapshotId))
                return Results.BadRequest(new { success = false, message = "SnapshotId is required.", errorCode = "BAD_REQUEST" });

            string safeName = Path.GetFileName(req.FileName);
            string safeCategory = SanitizeCategory(req.Category);

            try
            {
                GeneFile restored = dna.RestoreSessionSnapshot(id, safeCategory, safeName, req.SnapshotId);
                return Results.Ok(restored);
            }
            catch (FileNotFoundException ex)
            {
                return Results.NotFound(new { success = false, message = ex.Message, errorCode = "SNAPSHOT_NOT_FOUND" });
            }
        })
        .WithTags("SessionDNA");

        return endpoints;
    }

    private static string SanitizeCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return string.Empty;
        return string.Join("/",
            category.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(Path.GetFileName)
                    .Where(s => !string.IsNullOrWhiteSpace(s)));
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
