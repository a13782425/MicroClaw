using MicroClaw.Channel.Abstractions;
using MicroClaw.Channel.Abstractions.Models;

namespace MicroClaw.Channel.Feishu;

public sealed class FeishuChannel : IChannel
{
    public string Name => "Feishu";

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}