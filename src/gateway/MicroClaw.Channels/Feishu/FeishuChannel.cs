using MicroClaw.Channels.Models;

namespace MicroClaw.Channels.Feishu;

public sealed class FeishuChannel : IChannel
{
    public string Name => "Feishu";

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
