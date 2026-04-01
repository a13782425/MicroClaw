using System.Text.Json;
using Microsoft.Extensions.AI;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Hubs;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers;
using MicroClaw.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Jobs;

/// <summary>
/// 定时任务触发时的 AI 会话服务：
/// 向目标 Session 注入提示消息，调用 AI 获取回复，保存消息并通过 SignalR 通知前端。
/// </summary>
public sealed class SessionChatService(
    SessionStore sessionStore,
    ProviderConfigStore providerStore,
    ProviderClientFactory clientFactory,
    IHubContext<GatewayHub> hub,
    IUsageTracker usageTracker,
    ILogger<SessionChatService> logger)
{
    public async Task<string?> ExecuteAsync(string sessionId, string prompt, CancellationToken ct = default)
    {
        SessionInfo? session = sessionStore.Get(sessionId);
        if (session is null)
        {
            logger.LogWarning("CronJob: target session '{SessionId}' not found, skipping.", sessionId);
            return null;
        }

        ProviderConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == session.ProviderId);
        if (provider is null || !provider.IsEnabled)
        {
            logger.LogWarning("CronJob: provider '{ProviderId}' not found or disabled for session '{SessionId}'.",
                session.ProviderId, sessionId);
            return null;
        }

        // 保存定时触发的用户消息（标记来源为 cron，前端可据此过滤不显示）
        SessionMessage userMsg = new(
            Id: Guid.NewGuid().ToString("N"),
            Role: "user",
            Content: prompt,
            ThinkContent: null,
            Timestamp: DateTimeOffset.UtcNow,
            Attachments: null,
            Source: "cron");
        sessionStore.AddMessage(sessionId, userMsg);

        // 构建消息历史（含刚添加的用户消息）
        IReadOnlyList<SessionMessage> history = sessionStore.GetMessages(sessionId);
        List<ChatMessage> chatMessages = BuildChatMessages(history);

        IChatClient client = clientFactory.Create(provider);
        try
        {
            ChatResponse response = await client.GetResponseAsync(chatMessages, cancellationToken: ct);
            string assistantContent = response.Text ?? "（无回复）";

            SessionMessage assistantMsg = new(
                Id: Guid.NewGuid().ToString("N"),
                Role: "assistant",
                Content: assistantContent,
                ThinkContent: null,
                Timestamp: DateTimeOffset.UtcNow,
                Attachments: null);
            sessionStore.AddMessage(sessionId, assistantMsg);

            // 记录 Token 用量
            if (response.Usage is { } usage)
            {
                try
                {
                    long inputTokens = usage.InputTokenCount ?? 0L;
                    long outputTokens = usage.OutputTokenCount ?? 0L;
                    long cachedInputTokens = usage.CachedInputTokenCount ?? 0L;
                    long nonCachedInput = inputTokens - cachedInputTokens;

                    decimal inputCost = nonCachedInput > 0 && provider.Capabilities.InputPricePerMToken.HasValue
                        ? nonCachedInput * provider.Capabilities.InputPricePerMToken.Value / 1_000_000m : 0m;
                    decimal outputCost = provider.Capabilities.OutputPricePerMToken.HasValue
                        ? outputTokens * provider.Capabilities.OutputPricePerMToken.Value / 1_000_000m : 0m;
                    decimal cacheInputCost = cachedInputTokens > 0
                        ? cachedInputTokens * (provider.Capabilities.CacheInputPricePerMToken ?? provider.Capabilities.InputPricePerMToken ?? 0m) / 1_000_000m : 0m;

                    await usageTracker.TrackAsync(
                        sessionId,
                        provider.Id,
                        provider.DisplayName,
                        source: "cron",
                        inputTokens: inputTokens,
                        outputTokens: outputTokens,
                        cachedInputTokens: cachedInputTokens,
                        inputCostUsd: inputCost,
                        outputCostUsd: outputCost,
                        cacheInputCostUsd: cacheInputCost,
                        ct: ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to track cron token usage for session {SessionId}", sessionId);
                }
            }

            // 通知前端刷新该 Session 的消息
            await hub.Clients.All.SendAsync("cronJobExecuted",
                new { sessionId, content = assistantContent }, ct);

            return assistantContent;
        }
        finally
        {
            client.Dispose();
        }
    }

    private static List<ChatMessage> BuildChatMessages(IReadOnlyList<SessionMessage> history)
    {
        var messages = new List<ChatMessage>(history.Count);
        foreach (SessionMessage msg in history)
        {
            // ── 工具调用：还原为 MEAI FunctionCallContent ──────────────────
            if (msg.MessageType == "tool_call" && msg.Metadata is not null)
            {
                string? callId = msg.Metadata.TryGetValue("callId", out var cidEl) ? cidEl.GetString() : null;
                string? toolName = msg.Metadata.TryGetValue("toolName", out var tnEl) ? tnEl.GetString() : null;
                if (callId is not null && toolName is not null)
                {
                    IDictionary<string, object?>? args = msg.Metadata.TryGetValue("arguments", out var argsEl)
                        && argsEl.ValueKind == System.Text.Json.JsonValueKind.Object
                        ? argsEl.Deserialize<Dictionary<string, object?>>() : null;
                    messages.Add(new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent(callId, toolName, args)]));
                }
                continue;
            }

            // ── 工具结果：还原为 MEAI FunctionResultContent ─────────────────
            if (msg.MessageType == "tool_result" && msg.Metadata is not null)
            {
                string? callId = msg.Metadata.TryGetValue("callId", out var cidEl) ? cidEl.GetString() : null;
                if (callId is not null)
                {
                    messages.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(callId, msg.Content)]));
                }
                continue;
            }

            // ── 子 Agent / 其他系统消息：跳过 ────────────────────────────────
            if (msg.MessageType is "sub_agent_start" or "sub_agent_result")
                continue;
            if (msg.Role is "system" or "tool")
                continue;

            // ── 常规 user / assistant 消息 ───────────────────────────────────
            ChatRole role = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            messages.Add(new ChatMessage(role, msg.Content));
        }
        return messages;
    }
}
