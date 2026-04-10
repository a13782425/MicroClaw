namespace MicroClaw.Channels.Feishu;

/// <summary>
/// 请求飞书 WebSocket 渠道配置同步。
/// 由 <c>FeishuWebSocketSyncJob</c> 定时发布，<see cref="FeishuChannelProvider"/> 订阅并执行。
/// </summary>
public sealed record FeishuChannelSyncRequestedEvent;
