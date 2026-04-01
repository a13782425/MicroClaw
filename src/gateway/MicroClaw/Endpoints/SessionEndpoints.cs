using System.Security.Claims;
using System.Text.Json;
using MicroClaw.Agent;
using MicroClaw.Agent.Endpoints;
using MicroClaw.Agent.Memory;
using MicroClaw.Gateway.Contracts.Streaming;
using MicroClaw.Channels;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Hubs;
using MicroClaw.Providers;
using MicroClaw.Sessions;
using MicroClaw.Streaming;
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
        // GET /api/sessions — 获取顶层会话（子代理会话不对外暴露）
        endpoints.MapGet("/sessions", (SessionStore store) =>
            Results.Ok(store.AllTopLevel))
            .WithTags("Sessions");

        // POST /api/sessions — 创建会话
        endpoints.MapPost("/sessions", (CreateSessionRequest req, SessionStore store, ProviderConfigStore providerStore, AgentStore agentStore, ChannelConfigStore channelStore, SessionDnaService sessionDna) =>
        {
            if (string.IsNullOrWhiteSpace(req.Title))
                return Results.BadRequest(new { success = false, message = "Title is required.", errorCode = "BAD_REQUEST" });
            if (string.IsNullOrWhiteSpace(req.ProviderId))
                return Results.BadRequest(new { success = false, message = "ProviderId is required.", errorCode = "BAD_REQUEST" });

            ProviderConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == req.ProviderId);
            if (provider is null)
                return Results.NotFound(new { success = false, message = $"Provider '{req.ProviderId}' not found.", errorCode = "NOT_FOUND" });
            if (provider.ModelType == ModelType.Embedding)
                return Results.BadRequest(new { success = false, message = "Embedding providers cannot be bound to sessions.", errorCode = "BAD_REQUEST" });

            // 解析 ChannelId：默认使用内置 web channel
            string channelId = string.IsNullOrWhiteSpace(req.ChannelId)
                ? ChannelConfigStore.WebChannelId
                : req.ChannelId;
            ChannelConfig? channel = channelStore.GetById(channelId);
            if (channel is null)
                return Results.NotFound(new { success = false, message = $"Channel '{channelId}' not found.", errorCode = "NOT_FOUND" });

            // 解析 AgentId：默认使用 main agent
            string? agentId = string.IsNullOrWhiteSpace(req.AgentId)
                ? agentStore.GetDefault()?.Id
                : req.AgentId;
            if (!string.IsNullOrWhiteSpace(req.AgentId) && agentStore.GetById(req.AgentId) is null)
                return Results.NotFound(new { success = false, message = $"Agent '{req.AgentId}' not found.", errorCode = "NOT_FOUND" });

            SessionInfo created = store.Create(req.Title.Trim(), req.ProviderId, channel.ChannelType, channelId: channelId, agentId: agentId);
            sessionDna.InitializeSession(created.Id);
            return Results.Ok(created);
        })
        .WithTags("Sessions");

        // POST /api/sessions/delete — 删除会话
        endpoints.MapPost("/sessions/delete", (DeleteSessionRequest req, SessionStore store, SessionDnaService sessionDna) =>
        {
            if (string.IsNullOrWhiteSpace(req.Id))
                return Results.BadRequest(new { success = false, message = "Id is required.", errorCode = "BAD_REQUEST" });

            bool deleted = store.Delete(req.Id);
            if (!deleted)
                return Results.NotFound(new { success = false, message = $"Session '{req.Id}' not found.", errorCode = "NOT_FOUND" });

            // 同步删除会话固定 DNA 文件
            sessionDna.DeleteSessionDnaFiles(req.Id);
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
            if (provider.ModelType == ModelType.Embedding)
                return Results.BadRequest(new { success = false, message = "Embedding providers cannot be bound to sessions.", errorCode = "BAD_REQUEST" });

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
                var filtered = messages.Where(m => MessageVisibility.IsVisibleToFrontend(m.Visibility)).ToList();
                return Results.Ok(new { messages = filtered, total, hasMore = total > actualSkip + messages.Count });
            }

            var allMessages = store.GetMessages(id)
                .Where(m => MessageVisibility.IsVisibleToFrontend(m.Visibility)).ToList();
            return Results.Ok(allMessages);
        })
        .WithTags("Sessions");

        // POST /api/sessions/{id}/chat — SSE 流式对话
        endpoints.MapPost("/sessions/{id}/chat",
            async (string id, ChatRequest req, SessionStore store,
                   ProviderConfigStore providerStore,
                   AgentStore agentStore, AgentRunner agentRunner,
                   IEnumerable<IStreamItemPersistenceHandler> persistenceHandlers,
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
                    Id: Guid.NewGuid().ToString("N"),
                    Role: "user",
                    Content: req.Content,
                    ThinkContent: null,
                    Timestamp: DateTimeOffset.UtcNow,
                    Attachments: req.Attachments);
                store.AddMessage(id, userMessage);

                // 构建历史消息上下文
                IReadOnlyList<SessionMessage> history = store.GetMessages(id);

                // 获取 Session 绑定的 Agent（优先用绑定 AgentId，否则退到默认 Agent）
                AgentConfig? defaultAgent = string.IsNullOrWhiteSpace(session.AgentId)
                    ? agentStore.GetDefault()
                    : agentStore.GetById(session.AgentId) ?? agentStore.GetDefault();
                if (defaultAgent is null || !defaultAgent.IsEnabled)
                {
                    ctx.Response.StatusCode = 503;
                    await ctx.Response.WriteAsJsonAsync(new { message = "No enabled agent found for this session." }, ct);
                    return;
                }

                // 设置 SSE 响应头
                ctx.Response.ContentType = "text/event-stream; charset=utf-8";
                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.Headers.Connection = "keep-alive";
                ctx.Response.Headers["X-Accel-Buffering"] = "no";

                var persistencePipeline = new StreamItemPersistencePipeline(persistenceHandlers);

                try
                {
                    await foreach (StreamItem item in
                        agentRunner.StreamReActAsync(defaultAgent, session.ProviderId, history, id, ct))
                    {
                        // 持久化逻辑：通过管道分发
                        IReadOnlyList<SessionMessage> msgs = persistencePipeline.ProcessItem(item);
                        foreach (SessionMessage msg in msgs)
                            store.AddMessage(id, msg);

                        // 不可见于前端的事件跳过 SSE 推送（仅持久化供 LLM 使用）
                        if (!MessageVisibility.IsVisibleToFrontend(item.Visibility))
                            continue;

                        // 统一 SSE 序列化（所有类型）
                        await WriteSseAsync(ctx.Response, StreamItemSerializer.Serialize(item), ct);
                    }

                    // 从管道获取最终聚合的 assistant 消息（含文本 + think + 附件）
                    SessionMessage? finalMessage = persistencePipeline.Finalize();
                    if (finalMessage is not null)
                        store.AddMessage(id, finalMessage);

                    // 发送完成信号
                    string doneData = JsonSerializer.Serialize(new
                    {
                        type = "done",
                        thinkContent = finalMessage?.ThinkContent,
                        messageId = finalMessage?.Id
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

        // ── 会话 DNA 端点（固定两文件模式：USER / AGENTS）──────────────────────────────────
        // SOUL.md 已迁移至 Agent 级别（通过 /agents/{id}/dna 管理）

        // GET /api/sessions/{id}/dna — 列出固定 DNA 文件（USER / AGENTS）
        endpoints.MapGet("/sessions/{id}/dna", (string id, SessionStore store, SessionDnaService sessionDna) =>
        {
            if (store.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            return Results.Ok(sessionDna.ListFiles(id));
        })
        .WithTags("SessionDNA");

        // GET /api/sessions/{id}/dna/{fileName} — 读取指定固定 DNA 文件
        endpoints.MapGet("/sessions/{id}/dna/{fileName}", (string id, string fileName, SessionStore store, SessionDnaService sessionDna) =>
        {
            if (store.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            SessionDnaFileInfo? file = sessionDna.Read(id, fileName);
            return file is null
                ? Results.NotFound(new { success = false, message = $"File '{fileName}' is not a valid Session DNA file. Allowed: USER.md, AGENTS.md", errorCode = "NOT_FOUND" })
                : Results.Ok(file);
        })
        .WithTags("SessionDNA");

        // POST /api/sessions/{id}/dna — 更新固定 DNA 文件内容（body: fileName + content）
        endpoints.MapPost("/sessions/{id}/dna", (string id, SessionDnaUpdateRequest req, SessionStore store, SessionDnaService sessionDna) =>
        {
            if (store.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });
            if (string.IsNullOrWhiteSpace(req.FileName))
                return Results.BadRequest(new { success = false, message = "FileName is required.", errorCode = "BAD_REQUEST" });
            if (!SessionDnaService.IsAllowedFileName(req.FileName))
                return Results.BadRequest(new { success = false, message = $"'{req.FileName}' is not a valid Session DNA file. Allowed: USER.md, AGENTS.md (SOUL.md 已迁移至 Agent 级别)", errorCode = "INVALID_FILE_NAME" });

            SessionDnaFileInfo? updated = sessionDna.Update(id, req.FileName, req.Content ?? string.Empty);
            return updated is null
                ? Results.BadRequest(new { success = false, message = "Update failed.", errorCode = "BAD_REQUEST" })
                : Results.Ok(updated);
        })
        .WithTags("SessionDNA");

        // ── 会话记忆端点（B-02）────────────────────────────────────────────────────

        // GET /api/sessions/{id}/memory — 获取长期记忆（MEMORY.md）
        endpoints.MapGet("/sessions/{id}/memory", (string id, SessionStore store, MemoryService memory) =>
        {
            if (store.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            string content = memory.GetLongTermMemory(id);
            return Results.Ok(new { content });
        })
        .WithTags("SessionMemory");

        // POST /api/sessions/{id}/memory — 已禁用（长期记忆只读，改用 RAG chunk 管理）
        // 保留端点返回 405 Method Not Allowed，避免前端静默失败
        endpoints.MapPost("/sessions/{id}/memory", () =>
            Results.StatusCode(405))
        .WithTags("SessionMemory");

        // GET /api/sessions/{id}/memory/daily — 列出所有每日记忆（日期列表，降序）
        endpoints.MapGet("/sessions/{id}/memory/daily", (string id, SessionStore store, MemoryService memory) =>
        {
            if (store.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });

            IReadOnlyList<string> dates = memory.ListDailyMemories(id);
            return Results.Ok(new { dates });
        })
        .WithTags("SessionMemory");

        // GET /api/sessions/{id}/memory/daily/{date} — 获取指定日期记忆（YYYY-MM-DD）
        endpoints.MapGet("/sessions/{id}/memory/daily/{date}", (string id, string date, SessionStore store, MemoryService memory) =>
        {
            if (store.Get(id) is null)
                return Results.NotFound(new { success = false, message = $"Session '{id}' not found.", errorCode = "NOT_FOUND" });
            if (!MemoryService.IsValidDateFormat(date))
                return Results.BadRequest(new { success = false, message = $"Invalid date format: '{date}'. Expected YYYY-MM-DD.", errorCode = "BAD_REQUEST" });

            DailyMemoryInfo? info = memory.GetDailyMemory(id, date);
            return info is null
                ? Results.NotFound(new { success = false, message = $"No memory found for date '{date}'.", errorCode = "NOT_FOUND" })
                : Results.Ok(info);
        })
        .WithTags("SessionMemory");

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



    private static async Task WriteSseAsync(HttpResponse response, string data, CancellationToken ct)
    {
        await response.WriteAsync($"data: {data}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    /// <summary>将 Dictionary&lt;string, object?&gt; 转为 Dictionary&lt;string, JsonElement&gt; 以符合 SessionMessage.Metadata 类型。</summary>
    private static IReadOnlyDictionary<string, JsonElement> ToJsonElements(Dictionary<string, object?> dict)
    {
        string json = JsonSerializer.Serialize(dict, JsonOpts);
        Dictionary<string, JsonElement> result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOpts) ?? [];
        return result;
    }
}

// ── Session DNA Request records ────────────────────────────────────────────────────────

/// <summary>更新 Session 固定 DNA 文件的请求体。</summary>
public sealed record SessionDnaUpdateRequest(string FileName, string? Content);

/// <summary>更新 Session 长期记忆的请求体。</summary>
public sealed record UpdateMemoryRequest(string? Content);

// 以下 records 供旧 Agent DNA 端点等继续使用，待 M-05 清理时移除
public sealed record GeneFileWriteRequest(string FileName, string? Category, string? Content);
public sealed record GeneFileDeleteRequest(string FileName, string? Category);
public sealed record GeneFileRestoreRequest(string FileName, string? Category, string SnapshotId);
