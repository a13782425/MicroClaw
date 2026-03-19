using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Quartz;

namespace MicroClaw.Jobs;

/// <summary>
/// 基于 Quartz.NET 的定时任务调度器，实现 <see cref="ICronJobScheduler"/>。
/// 负责将 <see cref="CronJob"/> 注册/注销为 Quartz Trigger。
/// </summary>
public sealed class CronJobScheduler(ISchedulerFactory schedulerFactory, ILogger<CronJobScheduler> logger) : ICronJobScheduler
{
    private static JobKey JobKey(string jobId) => new(jobId, "cron");
    private static TriggerKey TriggerKey(string jobId) => new(jobId, "cron");

    public async Task ScheduleJobAsync(CronJob job, CancellationToken ct = default)
    {
        if (!job.IsEnabled) return;

        IScheduler scheduler = await schedulerFactory.GetScheduler(ct);

        IJobDetail jobDetail = JobBuilder.Create<CronJobQuartzJob>()
            .WithIdentity(JobKey(job.Id))
            .UsingJobData(CronJobQuartzJob.JobIdKey, job.Id)
            .StoreDurably()
            .Build();

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity(TriggerKey(job.Id))
            .ForJob(jobDetail)
            .WithCronSchedule(job.CronExpression, x => x.InTimeZone(TimeZoneInfo.Local))
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger, ct);
        logger.LogInformation("CronJobScheduler: scheduled job '{Name}' ({Id}) with expression '{Expr}'.",
            job.Name, job.Id, job.CronExpression);
    }

    public async Task UnscheduleJobAsync(string jobId, CancellationToken ct = default)
    {
        IScheduler scheduler = await schedulerFactory.GetScheduler(ct);
        await scheduler.DeleteJob(JobKey(jobId), ct);
        logger.LogInformation("CronJobScheduler: unscheduled job '{Id}'.", jobId);
    }

    public async Task RescheduleJobAsync(CronJob job, CancellationToken ct = default)
    {
        await UnscheduleJobAsync(job.Id, ct);
        if (job.IsEnabled)
            await ScheduleJobAsync(job, ct);
    }

    /// <summary>应用启动时，从数据库加载所有已启用任务并注册到 Quartz。</summary>
    public async Task StartupAsync(IReadOnlyList<CronJob> jobs, CancellationToken ct = default)
    {
        int count = 0;
        foreach (CronJob job in jobs.Where(j => j.IsEnabled))
        {
            try
            {
                await ScheduleJobAsync(job, ct);
                count++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "CronJobScheduler: failed to schedule job '{Name}' ({Id}) on startup.", job.Name, job.Id);
            }
        }
        logger.LogInformation("CronJobScheduler: loaded {Count}/{Total} cron jobs on startup.", count, jobs.Count);
    }
}
