using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Agent.ContextProviders;

/// <summary>
/// 扩展 <see cref="IAgentContextProvider"/>，在有当前用户消息时提供语义相关的上下文片段。
/// </summary>
/// <remarks>
/// 当 <see cref="AgentRunner"/> 知道当前用户消息时，优先调用此接口的
/// <see cref="BuildContextAsync(AgentConfig, string?, string?, CancellationToken)"/> 重载，
/// 以便实现类（如 <c>RagContextProvider</c>）能够基于用户消息进行语义检索，
/// 仅注入与当前对话相关的上下文段落，而非全量注入。
/// </remarks>
public interface IUserAwareContextProvider : IAgentContextProvider
{
    /// <summary>
    /// 构建并返回该 Provider 负责的上下文文本片段（携带用户消息）。
    /// </summary>
    /// <param name="agent">当前执行的 Agent 配置。</param>
    /// <param name="sessionId">当前会话 ID；子代理场景下可为 <c>null</c>。</param>
    /// <param name="userMessage">当前用户消息文本；为 <c>null</c> 时应回退至不依赖消息的默认行为。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>上下文文本；为 <c>null</c> 或空时忽略。</returns>
    ValueTask<string?> BuildContextAsync(
        AgentConfig agent,
        string? sessionId,
        string? userMessage,
        CancellationToken ct = default);
}
