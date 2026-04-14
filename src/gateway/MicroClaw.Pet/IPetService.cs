using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;

namespace MicroClaw.Pet;

/// <summary>
/// Pet 服务层接口：负责 Session/Pet 查找、惰性加载和消息路由。
/// <para>
/// 所有用户消息先到 Pet，由 Pet（LLM）决策选择模型/Agent/工具，再委派 AgentRunner 执行。
/// 未启用 Pet 的会话直接透传 AgentRunner，保持向后兼容。
/// </para>
/// </summary>
public interface IPetService
{
    /// <summary>
    /// 查找或惰性加载 Session 对应的 Pet 实例。
    /// </summary>
    /// <param name="session">目标会话。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>已加载的 <see cref="IPet"/>；若加载失败返回 <c>null</c>。</returns>
    Task<IPet?> GetOrLoadPetAsync(IMicroSession session, CancellationToken ct = default);

    /// <summary>
    /// 处理用户消息：查找 Session → 加载 Pet → 委托 Pet 执行。
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
