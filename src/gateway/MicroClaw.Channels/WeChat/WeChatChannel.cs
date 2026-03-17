using MicroClaw.Channels.Models;

namespace MicroClaw.Channels.WeChat;

public sealed class WeChatChannel : IChannel
{
    public string Name => "WeChat";

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
