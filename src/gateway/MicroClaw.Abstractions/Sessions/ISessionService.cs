using MicroClaw.Configuration.Options;

namespace MicroClaw.Abstractions.Sessions;

/// <summary>
/// Session 统一服务接口：合并了原 <c>IChannelSessionService</c> 和 <c>ISessionRepository</c> 的职责。
/// <para>
/// Endpoint 和渠道处理器通过此接口与会话系统交互；
/// AgentRunner 等需要只读仓储语义的组件仍通过 <c>ISessionRepository</c> 注入
/// （SessionService 同时实现两个接口）。
/// </para>
/// </summary>
public interface ISessionService : ISessionRepository
{
    // ── 渠道会话管理 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 按确定性 ID 查找渠道会话，不存在则自动创建（isApproved = false）。
    /// </summary>
    SessionInfo FindOrCreateSession(ChannelType channelType, string channelId, string senderId,
        string channelDisplayName, string providerId);

    /// <summary>通过 SignalR 通知管理员待批准会话（含 5 分钟限流）。</summary>
    Task NotifyPendingApprovalAsync(string sessionId, string sessionTitle, ChannelType channelType);

    /// <summary>
    /// 统一审批检查入口：若会话已审批返回 true；
    /// 若未审批，自动触发限流通知并返回 false。
    /// </summary>
    Task<bool> CheckApprovalAsync(SessionInfo session, ChannelType channelType);

    // ── 会话创建 ──────────────────────────────────────────────────────────────

    /// <summary>创建全新会话并返回领域对象（由 SessionEndpoints 调用）。</summary>
    Session CreateSession(string title, string providerId,
        ChannelType channelType = ChannelType.Web,
        string? id = null,
        string? agentId = null,
        string? parentSessionId = null,
        string? channelId = null);
}
