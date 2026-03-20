using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Infrastructure;

/// <summary>
/// 定时任务调度器抽象接口。
/// 实现类（基于 Quartz.NET）位于 MicroClaw 主项目。
/// </summary>
public interface ICronJobScheduler
{
    Task ScheduleJobAsync(CronJob job, CancellationToken ct = default);
    Task UnscheduleJobAsync(string jobId, CancellationToken ct = default);
    Task RescheduleJobAsync(CronJob job, CancellationToken ct = default);
    Task StartupAsync(IReadOnlyList<CronJob> jobs, CancellationToken ct = default);
}
