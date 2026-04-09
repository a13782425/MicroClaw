using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Abstractions.Channel;

public interface IChannelProvider
{
    string Name { get; }

    ChannelType Type { get; }

    /// <summary>渠道类型的本地化显示名称（用于 UI 渠道类型列表）。默认回退到 <see cref="Name"/>。</summary>
    string DisplayName => Name;

    /// <summary>是否允许用户通过 UI 创建此类型的渠道实例。内置渠道（如 Web）返回 false。</summary>
    bool CanCreate => true;

    IChannel Create(ChannelEntity config);

    Task PublishAsync(ChannelEntity config, ChannelMessage message, CancellationToken cancellationToken = default);

    Task<string?> HandleWebhookAsync(ChannelEntity config, string body, CancellationToken cancellationToken = default);

    Task<ChannelTestResult> TestConnectionAsync(ChannelEntity config, CancellationToken cancellationToken = default);
}
