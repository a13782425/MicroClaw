using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
using Microsoft.Extensions.AI;

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

    /// <summary>
    /// 处理渠道 Webhook 回调。<paramref name="headers"/> 供渠道实现内部做签名验证等安全检查。
    /// </summary>
    Task<WebhookResult> HandleWebhookAsync(string body, IReadOnlyDictionary<string, string?>? headers = null,
        CancellationToken cancellationToken = default);

    /// <summary>测试与渠道的连通性，返回连接状态和延迟。</summary>
    Task<ChannelTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>返回该渠道实例的运行时诊断信息，委托给对应的 <see cref="IChannelProvider"/>。</summary>
    Task<ChannelDiagnostics> GetDiagnosticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 接收 Session 转发的消息，执行渠道特定的业务处理。委托给对应的 <see cref="IChannelProvider"/>。
    /// 返回 null 表示该渠道不处理此消息。
    /// </summary>
    Task<string?> HandleSessionMessageAsync(SessionMessage message, SessionMessageContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create channel-specific AI tools for this channel instance.
    /// Default implementation returns an empty list.
    /// </summary>
    Task<IReadOnlyList<AIFunction>> CreateToolsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AIFunction>>([]);
}
