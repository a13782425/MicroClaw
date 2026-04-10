using MicroClaw.Channels.Feishu;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Jobs;

/// <summary>
/// F-F-3: 飞书 WebSocket 渠道配置同步 Job。
/// 每 30 秒检查数据库中的飞书 WebSocket 渠道配置变更，动态启停对应连接。
/// 原逻辑从 FeishuWebSocketManager.ExecuteAsync 中的 PeriodicTimer 提取至此，
/// 统一由 SystemJobRegistrar / Quartz 调度，与其他系统 Job 保持一致。
/// </summary>
public sealed class FeishuWebSocketSyncJob : IScheduledJob
{
    private readonly IFeishuWebSocketSync _wsSync;
    private readonly ILogger<FeishuWebSocketSyncJob> _logger;

    public FeishuWebSocketSyncJob(IServiceProvider sp)
    {
        _wsSync = sp.GetRequiredService<IFeishuWebSocketSync>();
        _logger = sp.GetRequiredService<ILogger<FeishuWebSocketSyncJob>>();
    }

    public string JobName => "feishu-websocket-sync";

    // 首次触发延后 45s，确保 FeishuWebSocketManager 的 StartAsync 完成初始同步后再开始轮询
    public JobSchedule Schedule => new JobSchedule.FixedInterval(
        Interval: TimeSpan.FromSeconds(30),
        StartupDelay: TimeSpan.FromSeconds(45));

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogDebug("FeishuWebSocketSyncJob: 开始检查渠道配置变更");
        await _wsSync.SyncChannelsAsync(ct);
        _logger.LogDebug("FeishuWebSocketSyncJob: 渠道配置同步完成");
    }
}
