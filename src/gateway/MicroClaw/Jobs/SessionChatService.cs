using System.Text.Json;
using Microsoft.Extensions.AI;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Hubs;
using MicroClaw.Providers;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Jobs;

/// <summary>
/// 定时任务触发时的 AI 会话服务：
/// 向目标 Session 注入提示消息，调用 AI 获取回复，保存消息并通过 SignalR 通知前端。
/// </summary>
public sealed class SessionChatService(
    ISessionService repo,
    ProviderService providerService,
    IHubContext<GatewayHub> hub,
    ILogger<SessionChatService> logger)
{
    public async Task<string?> ExecuteAsync(string sessionId, string prompt, CancellationToken ct = default)
    {
        IMicroSession? session = repo.Get(sessionId);
        if (session is null)
        {
            logger.LogWarning("CronJob: target session '{SessionId}' not found, skipping.", sessionId);
            return null;
        }

        ChatMicroProvider? chatProvider = providerService.TryGetProvider(session.ProviderId);
        if (chatProvider is null)
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
        repo.AddMessage(sessionId, userMsg);

        // 构建消息历史（含刚添加的用户消息）
        IReadOnlyList<SessionMessage> history = repo.GetMessages(sessionId);
        List<ChatMessage> chatMessages = BuildChatMessages(history);

        MicroChatContext chatCtx = MicroChatContext.ForSystem(session, "cron", ct);
        ChatResponse response = await chatProvider.ChatAsync(chatCtx, chatMessages);
        string assistantContent = response.Text ?? "（无回复）";

        SessionMessage assistantMsg = new(
            Id: Guid.NewGuid().ToString("N"),
            Role: "assistant",
            Content: assistantContent,
            ThinkContent: null,
            Timestamp: DateTimeOffset.UtcNow,
            Attachments: null);
        repo.AddMessage(sessionId, assistantMsg);

        // 通知前端刷新该 Session 的消息
        await hub.Clients.All.SendAsync("cronJobExecuted",
            new { sessionId, content = assistantContent }, ct);

        return assistantContent;
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
