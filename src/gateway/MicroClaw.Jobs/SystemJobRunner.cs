using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace MicroClaw.Jobs;

/// <summary>
/// Quartz IJob 适配器：将 Quartz 调度事件桥接到对应的 <see cref="IScheduledJob"/> 实现。
/// 通过 JobDataMap["jobName"] 定位目标 Job，再调用其 ExecuteAsync。
/// </summary>
[DisallowConcurrentExecution]
public sealed class SystemJobRunner(
    IServiceProvider serviceProvider,
    ILogger<SystemJobRunner> logger) : IJob
{
    internal const string JobNameKey = "jobName";
    internal const string JobGroup = "system";

    public async Task Execute(IJobExecutionContext context)
    {
        string? jobName = context.JobDetail.JobDataMap.GetString(JobNameKey);
        if (string.IsNullOrEmpty(jobName))
        {
            logger.LogError("SystemJobRunner: JobDataMap 中缺少 '{Key}' 键", JobNameKey);
            return;
        }

        IScheduledJob? job = serviceProvider.GetServices<IScheduledJob>()
            .FirstOrDefault(j => j.JobName == jobName);

        if (job is null)
        {
            logger.LogError("SystemJobRunner: 找不到名称为 '{JobName}' 的 IScheduledJob", jobName);
            return;
        }

        logger.LogInformation("SystemJobRunner: 开始执行 Job [{JobName}]", jobName);
        try
        {
            await job.ExecuteAsync(context.CancellationToken);
            logger.LogInformation("SystemJobRunner: Job [{JobName}] 执行完成", jobName);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("SystemJobRunner: Job [{JobName}] 已取消", jobName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SystemJobRunner: Job [{JobName}] 执行异常", jobName);
            throw new JobExecutionException(ex, refireImmediately: false);
        }
    }
}
