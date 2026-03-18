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
}
