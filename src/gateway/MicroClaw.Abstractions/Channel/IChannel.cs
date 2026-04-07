using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Abstractions.Channel;

public interface IChannel
{
    string Name { get; }

    ChannelType Type { get; }

    /// <summary>渠道的本地化显示名称（用于 UI 渠道类型列表）。默认回退到 <see cref="Name"/>。</summary>
    string DisplayName => Name;

    /// <summary>是否允许用户通过 UI 创建此类型的渠道实例。内置渠道（如 Web）返回 false。</summary>
    bool CanCreate => true;

    Task PublishAsync(ChannelMessage message, CancellationToken cancellationToken = default);

    Task<string?> HandleWebhookAsync(string body, ChannelEntity channelEntity, CancellationToken cancellationToken = default);

    /// <summary>测试与渠道的连通性，返回连接状态和延迟。</summary>
    Task<ChannelTestResult> TestConnectionAsync(ChannelEntity channelEntity, CancellationToken cancellationToken = default);
}