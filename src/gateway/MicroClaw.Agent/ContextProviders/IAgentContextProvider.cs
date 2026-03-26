using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Agent.ContextProviders;

/// <summary>
/// 聚合来自不同来源的 Agent 上下文片段，按 <see cref="Order"/> 顺序注入 System Prompt。
/// </summary>
/// <remarks>
/// 每个实现负责一个独立的上下文来源（Agent DNA / Session DNA / Session 记忆 / 自定义扩展）。
/// 返回 <c>null</c> 或空字符串时该片段被忽略，不影响其他 Provider。
/// </remarks>
public interface IAgentContextProvider
{
    /// <summary>Provider 的注入顺序（升序排列，数值越小越早注入到 System Prompt 中）。</summary>
    int Order { get; }

    /// <summary>
    /// 构建并返回该 Provider 负责的上下文文本片段。
    /// </summary>
    /// <param name="agent">当前执行的 Agent 配置。</param>
    /// <param name="sessionId">当前会话 ID；子代理场景下可为 <c>null</c>。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>上下文文本；为 <c>null</c> 或空时忽略。</returns>
    ValueTask<string?> BuildContextAsync(AgentConfig agent, string? sessionId, CancellationToken ct = default);
}
