using System.Collections.Concurrent;
using MicroClaw.Abstractions.Sessions;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.Memory;

/// <summary>
/// Interface for summarizing messages that overflow the context window.
/// </summary>
public interface IContextOverflowSummarizer
{
    /// <summary>
    /// Archive overflow messages: remove them from messages.jsonl and save as a pending
    /// summarization document. Idempotent: skips if the same overflow batch was already archived.
    /// </summary>
    Task SummarizeAsync(
        string sessionId,
        string providerId,
        IReadOnlyList<SessionMessage> overflowMessages,
        CancellationToken ct = default);
}

/// <summary>
/// When the context window overflows, this service:
/// 1. Writes the overflow messages to sessions/{id}/memory/pending/{ts}-{id}.jsonl
/// 2. Removes those messages from sessions/{id}/messages.jsonl
///
/// Actual summarization (LLM call, RAG ingest, MEMORY.md update) is deferred to
/// MemoryPendingProcessorJob which runs hourly during idle time.
///
/// Dedup: tracks lastArchivedMessageId per session to avoid re-archiving the same batch.
/// </summary>
public sealed class ContextOverflowSummarizer(
    MemoryService memoryService,
    ISessionService sessionRepository,
    ILogger<ContextOverflowSummarizer> logger) : IContextOverflowSummarizer
{
    // Per-session dedup: last overflow message ID that was archived
    private readonly ConcurrentDictionary<string, string> _lastArchivedMessageId = new();

    public Task SummarizeAsync(
        string sessionId,
        string providerId,
        IReadOnlyList<SessionMessage> overflowMessages,
        CancellationToken ct = default)
    {
        if (overflowMessages.Count == 0) return Task.CompletedTask;

        // Dedup: skip if the last overflow message was already archived
        string lastMsgId = overflowMessages[^1].Id;
        if (_lastArchivedMessageId.TryGetValue(sessionId, out string? prev) && prev == lastMsgId)
        {
            logger.LogDebug(
                "ContextOverflow: Session={SessionId} 溢出消息已归档过（lastMsgId={MsgId}），跳过",
                sessionId, lastMsgId);
            return Task.CompletedTask;
        }

        try
        {
            // 1. Write overflow messages as a pending file
            string fileName = memoryService.WritePendingMessages(sessionId, overflowMessages);

            // 2. Remove those messages from the active message history
            var ids = overflowMessages.Select(m => m.Id).ToHashSet();
            sessionRepository.RemoveMessages(sessionId, ids);

            // 3. Update dedup marker
            _lastArchivedMessageId[sessionId] = lastMsgId;

            logger.LogInformation(
                "ContextOverflow: Session={SessionId} 已将 {Count} 条溢出消息归档至 {File}，并从对话历史中移除",
                sessionId, overflowMessages.Count, fileName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "ContextOverflow: Session={SessionId} 溢出消息归档异常", sessionId);
        }

        return Task.CompletedTask;
    }
}

