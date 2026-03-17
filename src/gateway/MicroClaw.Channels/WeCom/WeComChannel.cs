using MicroClaw.Channels.Models;

namespace MicroClaw.Channels.WeCom;

public sealed class WeComChannel : IChannel
{
    public string Name => "WeCom";

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
