using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Jobs;

/// <summary>托管服务：应用启动后从数据库加载已有定时任务并注册到 Quartz 调度器。</summary>
public sealed class CronJobStartupService(CronJobStore cronJobStore, CronJobScheduler cronJobScheduler) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<CronJob> jobs = cronJobStore.GetAll();
        await cronJobScheduler.StartupAsync(jobs, cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
