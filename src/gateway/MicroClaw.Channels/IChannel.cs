using MicroClaw.Channels.Models;
using MicroClaw.Gateway.Contracts;

namespace MicroClaw.Channels;

public interface IChannel
{
    string Name { get; }

    ChannelType Type { get; }

    Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default);

    Task<string?> HandleWebhookAsync(string body, ChannelConfig channelConfig, CancellationToken cancellationToken = default);
}
