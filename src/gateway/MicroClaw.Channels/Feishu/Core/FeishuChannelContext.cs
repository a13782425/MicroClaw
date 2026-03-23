namespace MicroClaw.Channels.Feishu;

/// <summary>
/// 渠道级别上下文，注入到每个飞书 WebSocket 独立 ServiceProvider 中，
/// 让事件处理器知道当前处理的是哪个渠道。
/// </summary>
public sealed class FeishuChannelContext(ChannelConfig channel, FeishuChannelSettings settings)
{
    public ChannelConfig Channel { get; } = channel;
    public FeishuChannelSettings Settings { get; } = settings;
}
