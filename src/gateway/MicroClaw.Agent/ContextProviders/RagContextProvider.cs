using MicroClaw.Infrastructure.Data;
using MicroClaw.RAG;

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

    public RagContextProvider(IRagService ragService)
    {
        _ragService = ragService ?? throw new ArgumentNullException(nameof(ragService));
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

        // Session 作用域：会自动并行检索全局库 + 会话库并合并去重
        string content = await _ragService
            .QueryAsync(userMessage, RagScope.Session, sessionId, ct)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(content))
            return null;

        return $"## RAG 相关知识\n\n{content}";
    }
}
