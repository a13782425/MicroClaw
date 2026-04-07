using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Channels;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Sessions;

/// <summary>
/// 将 <see cref="IChannel"/>（处理某一渠道类型的所有实例）
/// 绑定到具体渠道配置 ID 后，包装为 <see cref="ISessionChannel"/>。
/// <para>
/// Session 持有此对象，可直接向具体收件人发送消息，
/// 无需关注底层渠道实现细节。
/// </para>
/// </summary>
internal sealed class BoundSessionChannel(
    IChannel channel,
    string channelId) : ISessionChannel
{
    public string ChannelId { get; } = channelId;
    public ChannelType Type { get; } = channel.Type;
    public string DisplayName { get; } = channel.DisplayName;

    public Task PublishTextAsync(string recipientId, string content, CancellationToken ct = default)
        => channel.PublishAsync(new ChannelMessage(recipientId, content, DateTimeOffset.UtcNow), ct);
}
