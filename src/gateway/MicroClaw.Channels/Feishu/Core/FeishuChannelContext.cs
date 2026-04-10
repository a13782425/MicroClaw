using FeishuNetSdk;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// 渠道级别上下文，注入到每个飞书 WebSocket 独立 ServiceProvider 中，
/// 让事件处理器知道当前处理的是哪个渠道及其 API 实例。
/// </summary>
internal sealed class FeishuChannelContext(ChannelEntity channel, FeishuChannelSettings settings)
{
    public ChannelEntity Channel { get; } = channel;
    public FeishuChannelSettings Settings { get; } = settings;

    /// <summary>
    /// 从子 SP 根作用域解析出的 <see cref="IFeishuTenantApi"/>，在 WebSocket 启动前由
    /// <see cref="FeishuChannel.CreateAsync"/> 通过 <see cref="SetApi"/> 注入，
    /// 生命周期与子 SP 相同，可安全在 fire-and-forget Task 中捕获。
    /// </summary>
    public IFeishuTenantApi Api { get; private set; } = null!;

    internal void SetApi(IFeishuTenantApi api) => Api = api;
}
