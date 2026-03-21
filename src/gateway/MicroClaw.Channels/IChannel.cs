using MicroClaw.Channels.Models;
using MicroClaw.Gateway.Contracts;

namespace MicroClaw.Channels;

public interface IChannel
{
    string Name { get; }

    ChannelType Type { get; }

    Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default);

    Task<string?> HandleWebhookAsync(string body, ChannelConfig channelConfig, CancellationToken cancellationToken = default);

    /// <summary>测试与渠道的连通性，返回连接状态和延迟。</summary>
    Task<ChannelTestResult> TestConnectionAsync(ChannelConfig config, CancellationToken cancellationToken = default);
}
