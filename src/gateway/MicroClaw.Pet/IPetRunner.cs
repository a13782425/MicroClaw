using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;

namespace MicroClaw.Pet;

/// <summary>
/// Pet 会话编排层接口。所有用户消息先到 Pet，由 Pet（LLM）决策选择模型/Agent/工具，再委派 AgentRunner 执行。
/// <para>
/// 未启用 Pet 的会话直接透传 AgentRunner，保持向后兼容。
/// </para>
/// </summary>
public interface IPetRunner
{
    /// <summary>
    /// 处理用户消息。
    /// <para>
    /// 流程：加载 PetState → PetDecisionEngine(LLM) → 根据 dispatch 结果调用 AgentRunner.StreamReActAsync()
    /// → 后处理（更新情绪、记录 journal）。未启用 Pet 时直接透传 AgentRunner。
    /// </para>
    /// </summary>
    /// <param name="sessionId">Session 唯一标识符。</param>
    /// <param name="history">完整消息历史。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="source">消息来源（"chat" / "channel"）。</param>
    /// <param name="channelId">渠道 ID（渠道消息时必填）。</param>
    IAsyncEnumerable<StreamItem> HandleMessageAsync(
        string sessionId,
        IReadOnlyList<SessionMessage> history,
        CancellationToken ct = default,
        string source = "chat",
        string? channelId = null);
}
