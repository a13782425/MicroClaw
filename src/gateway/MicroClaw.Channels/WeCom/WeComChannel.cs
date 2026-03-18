using MicroClaw.Channels.Models;
using MicroClaw.Gateway.Contracts;

namespace MicroClaw.Channels.WeCom;

public sealed class WeComChannel : IChannel
{
    public string Name => "WeCom";

    public ChannelType Type => ChannelType.WeCom;

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<string?> HandleWebhookAsync(string body, ChannelConfig channelConfig, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }
}
