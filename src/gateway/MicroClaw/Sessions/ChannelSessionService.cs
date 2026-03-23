using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MicroClaw.Channels;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MicroClaw.Sessions;

/// <summary>
/// 渠道会话服务：按发送者管理会话生命周期，通过 SignalR 通知管理员待批准会话。
/// </summary>
public sealed class ChannelSessionService(
    SessionStore store,
    IHubContext<GatewayHub> hubContext) : IChannelSessionService
{
    /// <summary>同一会话 5 分钟内只推一次通知。</summary>
    private static readonly ConcurrentDictionary<string, DateTimeOffset> NotifyThrottle = new();
    private static readonly TimeSpan ThrottleInterval = TimeSpan.FromMinutes(5);

    public SessionInfo FindOrCreateSession(ChannelType channelType, string channelId, string senderId,
        string channelDisplayName, string providerId)
    {
        string sessionId = GenerateSessionId(channelType, channelId, senderId);

        SessionInfo? existing = store.Get(sessionId);
        if (existing is not null) return existing;

        string channelLabel = ChannelConfigStore.SerializeChannelType(channelType);
        string senderShort = senderId.Length > 8 ? senderId[..8] : senderId;
        string title = $"{channelDisplayName}-{senderShort}";

        SessionInfo created = store.Create(title, providerId, channelType, id: sessionId, channelId: channelId);

        // 通知前端新会话已创建（fire-and-forget）
        _ = hubContext.Clients.All.SendAsync("sessionCreated", new
        {
            sessionId = created.Id,
            title = created.Title,
            channelType = channelLabel
        });

        return created;
    }

    public void AddMessage(string sessionId, SessionMessage message)
        => store.AddMessage(sessionId, message);

    public IReadOnlyList<SessionMessage> GetMessages(string sessionId)
        => store.GetMessages(sessionId);

    public async Task NotifyPendingApprovalAsync(string sessionId, string sessionTitle, ChannelType channelType)
    {
        // 限流：同一会话 5 分钟内只推一次
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (NotifyThrottle.TryGetValue(sessionId, out DateTimeOffset lastNotify)
            && now - lastNotify < ThrottleInterval)
            return;

        NotifyThrottle[sessionId] = now;

        await hubContext.Clients.All.SendAsync("sessionPendingApproval", new
        {
            sessionId,
            sessionTitle,
            channelType = ChannelConfigStore.SerializeChannelType(channelType),
            timestamp = now
        });
    }

    public async Task<bool> CheckApprovalAsync(SessionInfo session, ChannelType channelType)
    {
        if (session.IsApproved) return true;

        // 未审批：通知管理员（含限流），由调用方负责发送渠道特定的拒绝回复
        await NotifyPendingApprovalAsync(session.Id, session.Title, channelType);
        return false;
    }

    /// <summary>生成确定性会话 ID：SHA256(channelType + channelId + senderId) 的前 32 个十六进制字符。</summary>
    internal static string GenerateSessionId(ChannelType channelType, string channelId, string senderId)
    {
        string input = $"{channelType}:{channelId}:{senderId}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash)[..32];
    }
}
