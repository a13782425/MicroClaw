namespace MicroClaw.Gateway.Contracts.Sessions;

/// <summary>
/// 渠道消息处理器通过此接口管理会话，由主项目实现。
/// </summary>
public interface IChannelSessionService
{
    /// <summary>
    /// 按确定性 ID 查找渠道会话，不存在则自动创建（isApproved = false）。
    /// </summary>
    SessionInfo FindOrCreateSession(ChannelType channelType, string channelId, string senderId,
        string channelDisplayName, string providerId);

    void AddMessage(string sessionId, SessionMessage message);

    IReadOnlyList<SessionMessage> GetMessages(string sessionId);

    /// <summary>通过 SignalR 通知管理员待批准会话（含限流）。</summary>
    Task NotifyPendingApprovalAsync(string sessionId, string sessionTitle, ChannelType channelType);

    /// <summary>
    /// 统一审批检查入口：若会话已审批返回 true；若未审批，自动触发限流通知并返回 false。
    /// 渠道处理器应在调用方返回 false 后自行发送渠道特定的拒绝提示。
    /// </summary>
    Task<bool> CheckApprovalAsync(SessionInfo session, ChannelType channelType);
}
