using MicroClaw.Channels.Models;
using MicroClaw.Gateway.Contracts;

namespace MicroClaw.Channels.WeChat;

public sealed class WeChatChannel : IChannel
{
    public string Name => "WeChat";

    public ChannelType Type => ChannelType.WeChat;

    public Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<string?> HandleWebhookAsync(string body, ChannelConfig channelConfig, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<string?>(null);
    }

    public Task<ChannelTestResult> TestConnectionAsync(ChannelConfig config, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new ChannelTestResult(false, "微信渠道连通性测试尚未实现", 0));
    }
}
