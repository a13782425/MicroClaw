using MicroClaw.Channel.Abstractions;
using MicroClaw.Channel.Abstractions.Models;

namespace MicroClaw.Channel.WeChat;

public sealed class WeChatChannel : IChannel
{
    public string Name => "WeChat";

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}