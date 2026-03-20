using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Jobs;

/// <summary>托管服务：应用启动后从数据库加载已有定时任务并注册到 Quartz 调度器。</summary>
public sealed class CronJobStartupService(
    CronJobStore cronJobStore,
    ICronJobScheduler cronJobScheduler,
    ILogger<CronJobStartupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<CronJob> jobs = cronJobStore.GetAll();
            await cronJobScheduler.StartupAsync(jobs, cancellationToken);
            logger.LogInformation("CronJobStartupService: initialized successfully, loaded {Count} jobs.", jobs.Count);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "CronJobStartupService: failed to initialize cron jobs.");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
