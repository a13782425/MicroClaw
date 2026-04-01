using MicroClaw.Infrastructure.Data;
using MicroClaw.RAG;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.ContextProviders;

/// <summary>
/// RAG 上下文提供者 — 通过 <see cref="IRagService"/> 对全局及会话知识库进行语义检索，
/// 将与当前用户消息最相关的段落注入 System Prompt。
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>依赖 <see cref="IUserAwareContextProvider"/>：仅当 <c>userMessage</c> 不为空时执行检索；否则跳过注入。</item>
///   <item>Order 15：在 AgentDnaContextProvider(10) 之后、SessionDnaContextProvider(20) 之前注入，
///   作为全局知识背景层。</item>
///   <item>检索策略：调用 <see cref="IRagService.QueryAsync"/> 并传入 <see cref="RagScope.Session"/>，
///   内部自动合并全局库 + 会话库的结果（双库并行检索）。</item>
/// </list>
/// </remarks>
public sealed class RagContextProvider : IUserAwareContextProvider
{
    private readonly IRagService _ragService;
    private readonly RagRetrievalContext _retrievalContext;
    private readonly ILogger<RagContextProvider> _logger;

    public RagContextProvider(
        IRagService ragService,
        RagRetrievalContext retrievalContext,
        ILogger<RagContextProvider> logger)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
        _retrievalContext = retrievalContext ?? throw new ArgumentNullException(nameof(retrievalContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    /// <remarks>Order 15：位于 Agent DNA(10) 和 Session DNA(20) 之间。</remarks>
    public int Order => 15;

    /// <inheritdoc/>
    /// <remarks>
    /// 无 <c>userMessage</c> 上下文时无法进行语义检索，直接返回 <c>null</c> 跳过注入。
    /// </remarks>
    public ValueTask<string?> BuildContextAsync(
        AgentConfig agent,
        string? sessionId,
        CancellationToken ct = default)
        => new(default(string));   // 无 userMessage，跳过

    /// <inheritdoc/>
    public async ValueTask<string?> BuildContextAsync(
        AgentConfig agent,
        string? sessionId,
        string? userMessage,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
            return null;

        try
        {
            // 确定检索作用域：有 sessionId 时使用 Session（合并全局+会话），否则仅检索全局
            RagScope scope = string.IsNullOrWhiteSpace(sessionId) ? RagScope.Global : RagScope.Session;

            _logger.LogInformation("RAG 检索开始 (session={SessionId}, scope={Scope}, query={Query})",
                sessionId, scope, userMessage.Length > 60 ? userMessage[..60] + "..." : userMessage);

            RagQueryResult result = await _ragService
                .QueryWithMetadataAsync(userMessage, scope, sessionId, ct)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(result.Content))
            {
                _logger.LogInformation("RAG 检索无命中，跳过注入 (session={SessionId})", sessionId);
                return null;
            }

            // 将检索到的 chunk 引用写入 scoped 上下文，供后续 AI 审计使用
            _retrievalContext.RetrievedChunks = result.RetrievedChunks;

            _logger.LogInformation("RAG 检索命中 {ChunkCount} 个 chunk，已注入上下文 (session={SessionId}, contentLen={Len})",
                result.RetrievedChunks.Count, sessionId, result.Content.Length);
            return $"## RAG 相关知识\n\n{result.Content}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RAG 上下文检索失败，跳过 RAG 注入 (session={SessionId})", sessionId);
            return null;
        }
    }
}
