using MicroClaw.Abstractions.Pet;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Abstractions.Sessions;

/// <summary>
/// Session 持有的渠道窄接口（接口隔离原则）。
/// <para>
/// 比 <c>IChannel</c>（定义于 <c>MicroClaw.Abstractions.Channel</c>）更轻量，
/// 仅暴露 Session 层所需的标识和发送能力，Session 组件无需了解 webhook 处理等渠道内部细节。
/// </para>
/// <para>
/// 所有 <c>IChannel</c> 实现均应同时实现此接口；Web 内置渠道使用独立的 <c>WebSessionChannel</c>。
/// </para>
/// </summary>
public interface ISessionChannel
{
    /// <summary>渠道配置 ID（对应 ChannelConfigStore 中的记录）。</summary>
    string ChannelId { get; }

    /// <summary>渠道类型。</summary>
    ChannelType Type { get; }

    /// <summary>渠道本地化显示名称。</summary>
    string DisplayName { get; }

    /// <summary>向指定接收者发送文本消息。</summary>
    Task PublishTextAsync(string recipientId, string content, CancellationToken ct = default);
}
