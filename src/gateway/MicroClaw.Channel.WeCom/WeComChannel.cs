using MicroClaw.Channel.Abstractions;
using MicroClaw.Channel.Abstractions.Models;

namespace MicroClaw.Channel.WeCom;

public sealed class WeComChannel : IChannel
{
    public string Name => "WeCom";

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}