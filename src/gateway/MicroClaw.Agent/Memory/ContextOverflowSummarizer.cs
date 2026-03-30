using System.Collections.Concurrent;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Providers;
using MicroClaw.RAG;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.Memory;

/// <summary>
/// Interface for summarizing messages that overflow the context window.
/// </summary>
public interface IContextOverflowSummarizer
{
    /// <summary>
    /// Summarize overflow messages into daily memory and ingest into session RAG.
    /// Idempotent: skips if the same overflow batch was already summarized.
    /// </summary>
    Task SummarizeAsync(
        string sessionId,
        string providerId,
        IReadOnlyList<SessionMessage> overflowMessages,
        CancellationToken ct = default);
}

/// <summary>
/// When the context window overflows, this service:
/// 1. Reads existing daily memory for today
/// 2. Calls the session's bound model to summarize overflow messages merged with existing memory
/// 3. Writes updated summary to sessions/{id}/memory/YYYY-MM-DD.md
/// 4. Ingests the summary into session RAG (sourceId = memory:YYYY-MM-DD)
///
/// Dedup: tracks lastSummarizedMessageId per session to avoid re-summarizing the same batch.
/// </summary>
public sealed class ContextOverflowSummarizer(
    MemoryService memoryService,
    ProviderConfigStore providerStore,
    ProviderClientFactory clientFactory,
    IRagService ragService,
    ILogger<ContextOverflowSummarizer> logger) : IContextOverflowSummarizer
{
    // Per-session dedup: last overflow message ID that was summarized
    private readonly ConcurrentDictionary<string, string> _lastSummarizedMessageId = new();

    internal const string SummaryPromptTemplate =
        """
        你是一个记忆管理助手。请将以下对话和已有的今日记忆合并总结为精简的记忆笔记。
        保留关键信息、用户意图、重要决策和待办事项。控制在300字以内。
        输出格式：直接输出 Markdown 正文，无需标题，每个要点一行，以 `-` 开头。

        ## 已有今日记忆
        {existing}

        ## 需要总结的对话
        {messages}
        """;

    public async Task SummarizeAsync(
        string sessionId,
        string providerId,
        IReadOnlyList<SessionMessage> overflowMessages,
        CancellationToken ct = default)
    {
        if (overflowMessages.Count == 0) return;

        // Dedup: skip if the last overflow message was already summarized
        string lastMsgId = overflowMessages[^1].Id;
        if (_lastSummarizedMessageId.TryGetValue(sessionId, out string? prev) && prev == lastMsgId)
        {
            logger.LogDebug("ContextOverflow: Session={SessionId} 溢出消息已总结过（lastMsgId={MsgId}），跳过",
                sessionId, lastMsgId);
            return;
        }

        ProviderConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == providerId && p.IsEnabled);
        if (provider is null)
        {
            logger.LogWarning("ContextOverflow: Session={SessionId} 无可用 Provider（{ProviderId}），跳过总结",
                sessionId, providerId);
            return;
        }

        try
        {
            string dateStr = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");

            // 1. Read existing daily memory
            DailyMemoryInfo? existing = memoryService.GetDailyMemory(sessionId, dateStr);
            string existingText = existing?.Content ?? "（暂无今日记忆）";

            // 2. Format overflow messages
            string formatted = FormatMessages(overflowMessages);

            // 3. Call LLM to summarize
            string prompt = SummaryPromptTemplate
                .Replace("{existing}", existingText)
                .Replace("{messages}", formatted);

            IChatClient client = clientFactory.Create(provider);
            ChatResponse response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, prompt)],
                cancellationToken: ct);

            string summary = response.Text ?? string.Empty;
            if (string.IsNullOrWhiteSpace(summary))
            {
                logger.LogWarning("ContextOverflow: Session={SessionId} LLM 返回空总结", sessionId);
                return;
            }

            // 4. Write to daily memory file
            memoryService.WriteDailyMemory(sessionId, dateStr, summary);

            // 5. Ingest into session RAG (delete old first, then re-ingest)
            string sourceId = $"memory:{dateStr}";
            await ragService.DeleteBySourceIdAsync(sourceId, RagScope.Session, sessionId, ct);
            await ragService.IngestAsync(summary, sourceId, RagScope.Session, sessionId, ct);

            // 6. Update dedup marker
            _lastSummarizedMessageId[sessionId] = lastMsgId;

            logger.LogInformation(
                "ContextOverflow: Session={SessionId} 溢出 {Count} 条消息已总结写入 {Date} 并索引到 RAG",
                sessionId, overflowMessages.Count, dateStr);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ContextOverflow: Session={SessionId} 总结异常", sessionId);
        }
    }

    internal static string FormatMessages(IReadOnlyList<SessionMessage> messages)
    {
        return string.Join("\n", messages
            .Where(m => m.Role is "user" or "assistant")
            .Select(m =>
            {
                string role = m.Role == "user" ? "用户" : "AI";
                string content = m.Content.Length > 500 ? m.Content[..500] + "..." : m.Content;
                return $"[{role}]: {content}";
            }));
    }
}
