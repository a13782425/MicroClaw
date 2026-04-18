using MicroClaw.Configuration.Options;

namespace MicroClaw.Abstractions.Sessions;
/// <summary>
/// Unified session service contract: session CRUD, message persistence, and channel session management.
/// 实现方负责将会话作为 <c>MicroObject</c> 管理，并将消息持久化委托给 <c>SessionMessagesComponent</c>。
/// </summary>
public interface ISessionService
{
    // ── Session CRUD ──────────────────────────────────────────────────────
    
    IMicroSession? Get(string id);

    IReadOnlyList<IMicroSession> GetAll();

    void Save(IMicroSession microSession);

    bool Delete(string id);

    // ── Message persistence ──────────────────────────────────────────────

    void AddMessage(string sessionId, SessionMessage message);

    IReadOnlyList<SessionMessage> GetMessages(string sessionId);

    (IReadOnlyList<SessionMessage> Messages, int Total) GetMessagesPaged(
        string sessionId, int skip, int limit);

    void RemoveMessages(string sessionId, IReadOnlySet<string> messageIds);

    // ── Channel session management ───────────────────────────────────────
    
    Task<SessionInfo> FindOrCreateSession(ChannelType channelType, string channelId, string senderId, string channelDisplayName, string providerId);
    
    Task NotifyPendingApprovalAsync(string sessionId, string sessionTitle, ChannelType channelType);
    
    Task<bool> CheckApprovalAsync(SessionInfo session, ChannelType channelType);
    
    Task<IMicroSession> CreateSession(string title, string providerId, ChannelType channelType = ChannelType.Web, string? id = null, string? agentId = null, string? channelId = null);
}
