using MicroClaw.Abstractions.Events;
using MicroClaw.Channels.Feishu;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Jobs;

/// <summary>
/// 飞书 WebSocket 渠道配置同步 Job。
/// 每 30 秒发布 <see cref="FeishuChannelSyncRequestedEvent"/>，
/// 由 <c>FeishuChannelProvider</c> 订阅并执行渠道配置变更同步。
/// </summary>
public sealed class FeishuWebSocketSyncJob : IScheduledJob
{
    private readonly IAsyncEventBus _eventBus;
    private readonly ILogger<FeishuWebSocketSyncJob> _logger;

    public FeishuWebSocketSyncJob(IServiceProvider sp)
    {
        _eventBus = sp.GetRequiredService<IAsyncEventBus>();
        _logger = sp.GetRequiredService<ILogger<FeishuWebSocketSyncJob>>();
    }

    public string JobName => "feishu-websocket-sync";

    // 首次触发延后 45s，确保 FeishuChannelProvider 的 StartAsync 完成初始同步后再开始轮询
    public JobSchedule Schedule => new JobSchedule.FixedInterval(
        Interval: TimeSpan.FromSeconds(30),
        StartupDelay: TimeSpan.FromSeconds(45));

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogDebug("FeishuWebSocketSyncJob: 开始检查渠道配置变更");
        await _eventBus.PublishAsync(new FeishuChannelSyncRequestedEvent(), ct);
        _logger.LogDebug("FeishuWebSocketSyncJob: 渠道配置同步完成");
    }
}
