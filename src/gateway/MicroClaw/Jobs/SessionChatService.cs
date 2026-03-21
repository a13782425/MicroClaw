using Microsoft.Extensions.AI;
using MicroClaw.Gateway.Contracts.Sessions;
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
                    await usageTracker.TrackAsync(
                        sessionId,
                        provider.Id,
                        provider.DisplayName,
                        source: "cron",
                        inputTokens: usage.InputTokenCount ?? 0L,
                        outputTokens: usage.OutputTokenCount ?? 0L,
                        inputPricePerMToken: provider.Capabilities.InputPricePerMToken,
                        outputPricePerMToken: provider.Capabilities.OutputPricePerMToken,
                        ct);
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
            ChatRole role = msg.Role.Equals("user", StringComparison.OrdinalIgnoreCase)
                ? ChatRole.User
                : ChatRole.Assistant;
            messages.Add(new ChatMessage(role, msg.Content));
        }
        return messages;
    }
}
