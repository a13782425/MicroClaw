namespace MicroClaw.Abstractions.Sessions;

/// <summary>
/// Channel 处理 Session 转发消息时所需的上下文信息。
/// 由 Session 层构建，传入 <see cref="MicroClaw.Abstractions.Channel.IChannelProvider.HandleSessionMessageAsync"/>。
/// </summary>
public sealed record SessionMessageContext(
    string SessionId,
    string? SenderId,
    string? ChatId,
    string ChannelId);
