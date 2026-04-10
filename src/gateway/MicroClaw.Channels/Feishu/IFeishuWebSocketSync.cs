namespace MicroClaw.Channels.Feishu;

/// <summary>
/// 飞书 WebSocket 连接同步接口，供定时 Job 调用，无需引用内部 FeishuWebSocketManager。
/// </summary>
public interface IFeishuWebSocketSync
{
    /// <summary>检查渠道配置变更，动态启停对应的 WebSocket 连接。</summary>
    Task SyncChannelsAsync(CancellationToken ct = default);
}
