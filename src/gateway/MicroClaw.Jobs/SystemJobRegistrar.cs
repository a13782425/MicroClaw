using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace MicroClaw.Jobs;

/// <summary>
/// 系统 Job 注册器。
/// 应用启动时遍历所有 <see cref="IScheduledJob"/> 实现，
/// 将其转换为 Quartz <see cref="IJobDetail"/> + <see cref="ITrigger"/> 并注册到调度器。
/// </summary>
public sealed class SystemJobRegistrar(
    ISchedulerFactory schedulerFactory,
    IEnumerable<IScheduledJob> jobs,
    ILogger<SystemJobRegistrar> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IScheduler scheduler = await schedulerFactory.GetScheduler(cancellationToken);
        List<IScheduledJob> jobList = jobs.ToList();
        logger.LogInformation("SystemJobRegistrar: 正在注册 {Count} 个系统 Job", jobList.Count);

        foreach (IScheduledJob job in jobList)
        {
            IJobDetail detail = BuildJobDetail(job);
            ITrigger trigger = BuildTrigger(job);

            await scheduler.ScheduleJob(detail, trigger, cancellationToken);

            DateTimeOffset? next = trigger.GetNextFireTimeUtc();
            logger.LogInformation(
                "SystemJobRegistrar: [{JobName}] 已注册，下次触发时间 {Next:yyyy-MM-dd HH:mm:ss} UTC",
                job.JobName,
                next.HasValue ? next.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss") : "未知");
        }

        logger.LogInformation("SystemJobRegistrar: 全部 {Count} 个系统 Job 注册完成", jobList.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static IJobDetail BuildJobDetail(IScheduledJob job) =>
        JobBuilder.Create<SystemJobRunner>()
            .WithIdentity(job.JobName, SystemJobRunner.JobGroup)
            .UsingJobData(SystemJobRunner.JobNameKey, job.JobName)
            .StoreDurably(false)
            .Build();

    private static ITrigger BuildTrigger(IScheduledJob job) =>
        job.Schedule switch
        {
            JobSchedule.FixedInterval fi => BuildFixedIntervalTrigger(job.JobName, fi),
            JobSchedule.DailyAt da       => BuildDailyAtTrigger(job.JobName, da),
            _                            => throw new NotSupportedException($"未支持的调度类型: {job.Schedule.GetType().Name}")
        };

    private static ITrigger BuildFixedIntervalTrigger(string jobName, JobSchedule.FixedInterval fi)
    {
        DateTimeOffset startAt = DateTimeOffset.UtcNow.Add(fi.StartupDelay);
        int intervalSeconds = Math.Max(1, (int)fi.Interval.TotalSeconds);

        return TriggerBuilder.Create()
            .WithIdentity($"{jobName}-trigger", SystemJobRunner.JobGroup)
            .StartAt(startAt)
            .WithSimpleSchedule(s => s
                .WithIntervalInSeconds(intervalSeconds)
                .RepeatForever())
            .Build();
    }

    private static ITrigger BuildDailyAtTrigger(string jobName, JobSchedule.DailyAt da)
    {
        DateTimeOffset startAt = DateTimeOffset.UtcNow.Add(da.StartupDelay);

        // Cron: 秒 分 时 日 月 周
        string cron = $"0 {da.TimeUtc.Minute} {da.TimeUtc.Hour} * * ?";

        return TriggerBuilder.Create()
            .WithIdentity($"{jobName}-trigger", SystemJobRunner.JobGroup)
            .StartAt(startAt)
            .WithCronSchedule(cron, c => c.InTimeZone(TimeZoneInfo.Utc))
            .Build();
    }
}
