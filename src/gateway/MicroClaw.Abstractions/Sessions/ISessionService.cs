using MicroClaw.Configuration.Options;

namespace MicroClaw.Abstractions.Sessions;
/// <summary>
/// Unified session service contract for channel session management and persistence.
/// </summary>
public interface ISessionService : ISessionRepository, IService
{
    Task<SessionInfo> FindOrCreateSession(ChannelType channelType, string channelId, string senderId, string channelDisplayName, string providerId);
    
    Task NotifyPendingApprovalAsync(string sessionId, string sessionTitle, ChannelType channelType);
    
    Task<bool> CheckApprovalAsync(SessionInfo session, ChannelType channelType);
    
    Task<IMicroSession> CreateSession(string title, string providerId, ChannelType channelType = ChannelType.Web, string? id = null, string? agentId = null, string? channelId = null);
}