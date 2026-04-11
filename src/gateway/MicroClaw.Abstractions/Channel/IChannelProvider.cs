using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
using Microsoft.Extensions.AI;

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

    /// <summary>
    /// 处理渠道 Webhook 回调。<paramref name="headers"/> 供渠道实现内部做签名验证等安全检查。
    /// </summary>
    Task<WebhookResult> HandleWebhookAsync(ChannelEntity config, string body,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default);

    Task<ChannelTestResult> TestConnectionAsync(ChannelEntity config, CancellationToken cancellationToken = default);

    /// <summary>
    /// 返回该渠道的运行时诊断信息（连接状态、Token TTL、最近消息结果、错误统计等）。
    /// 各渠道 Provider 按需重写，填充 <see cref="ChannelDiagnostics.Extra"/> 中的渠道特有字段。
    /// 默认实现返回基础 ok 状态。
    /// </summary>
    Task<ChannelDiagnostics> GetDiagnosticsAsync(ChannelEntity config, CancellationToken cancellationToken = default)
        => Task.FromResult(ChannelDiagnostics.Ok(config.Id, Type.ToString()));

    /// <summary>
    /// Channel 接收 Session 转发的消息，执行渠道特定的业务处理（如云文档操作、表格写入）。
    /// 不包含 AI 逻辑，仅做渠道级别的操作。返回 null 表示该渠道不处理此消息。
    /// </summary>
    Task<string?> HandleSessionMessageAsync(ChannelEntity config, SessionMessage message,
        SessionMessageContext context, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    // ── Lifecycle Hooks (driven by ChannelRunner) ───────────────────────

    /// <summary>Provider 启动时由 ChannelRunner 调用，用于初始化长连接等资源。默认空实现。</summary>
    Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>Provider 停止时由 ChannelRunner 调用，用于释放长连接等资源。默认空实现。</summary>
    Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>由 ChannelRunner 定时调用（默认 30s），用于同步配置、断线重连等周期任务。默认空实现。</summary>
    Task TickAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    // ── Tool Management (delegated via ChannelToolBridge) ───────────────

    /// <summary>返回此渠道 Provider 提供的工具元数据描述列表（不需要运行时上下文，用于 UI 展示）。默认空列表。</summary>
    IReadOnlyList<(string Name, string Description)> GetToolDescriptions() => [];

    /// <summary>
    /// 按指定渠道实例 ID 创建工具列表。由 ChannelToolBridge 桥接调用。
    /// 默认返回空列表，飞书等渠道重写此方法以按凭据创建渠道工具。
    /// </summary>
    Task<IReadOnlyList<AIFunction>> CreateToolsAsync(string channelId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AIFunction>>([]);
}
