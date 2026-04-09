using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Abstractions.Channel;

public interface IChannel
{
    string Id { get; }

    string Name { get; }

    ChannelType Type { get; }

    ChannelEntity Config { get; }

    /// <summary>渠道实例的本地化显示名称（优先使用配置名）。</summary>
    string DisplayName => Name;

    Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default);

    Task<string?> HandleWebhookAsync(string body, CancellationToken cancellationToken = default);

    /// <summary>测试与渠道的连通性，返回连接状态和延迟。</summary>
    Task<ChannelTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);
}
