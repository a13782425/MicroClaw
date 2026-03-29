using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.RAG;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.Sessions;

/// <summary>
/// <see cref="ISessionMessageIndexer"/> 实现。
/// <para>
/// 过滤 <c>user</c>/<c>assistant</c> 角色的可见消息，跳过已索引的（以 <c>msg:{id}</c> 为 SourceId 去重），
/// 将新消息以 <c>"{role}: {content}"</c> 格式写入 Session RAG DB，支持增量幂等索引。
/// </para>
/// </summary>
public sealed class SessionMessageIndexer : ISessionMessageIndexer
{
    private readonly IRagService _ragService;
    private readonly ILogger<SessionMessageIndexer> _logger;

    public SessionMessageIndexer(IRagService ragService, ILogger<SessionMessageIndexer> logger)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task IndexNewMessagesAsync(
        string sessionId,
        IReadOnlyList<SessionMessage> messages,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        try
        {
            // 1. 查询已索引的 SourceId 集合（增量去重）
            IReadOnlySet<string> indexed =
                await _ragService.GetIndexedSourceIdsAsync(RagScope.Session, sessionId, ct)
                    .ConfigureAwait(false);

            // 2. 过滤出需要索引的消息：
            //    - 仅 user / assistant 角色
            //    - Content 非空
            //    - 排除完全内部消息（Internal 对 LLM 和前端均不可见）
            //    - 排除已索引的消息（SourceId = msg:{message.Id}）
            var toIndex = messages
                .Where(m => m.Role is "user" or "assistant")
                .Where(m => !string.IsNullOrWhiteSpace(m.Content))
                .Where(m => m.Visibility != MessageVisibility.Internal)
                .Where(m => !indexed.Contains($"msg:{m.Id}"))
                .ToList();

            if (toIndex.Count == 0) return;

            _logger.LogInformation(
                "RAG 增量索引开始 session={SessionId}：共 {Total} 条消息，待索引 {Count} 条",
                sessionId, messages.Count, toIndex.Count);

            // 3. 逐条向量化写入（每条消息使用稳定 SourceId，支持准确去重）
            foreach (SessionMessage msg in toIndex)
            {
                string sourceId = $"msg:{msg.Id}";
                string sourceText = $"{msg.Role}: {msg.Content}";
                await _ragService
                    .IngestAsync(sourceText, sourceId, RagScope.Session, sessionId, ct)
                    .ConfigureAwait(false);
            }

            _logger.LogInformation(
                "RAG 增量索引完成 session={SessionId}：已写入 {Count} 条消息",
                sessionId, toIndex.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 索引失败不应中断对话流程，仅记录错误
            _logger.LogError(ex, "RAG 增量索引失败 session={SessionId}", sessionId);
        }
    }
}
