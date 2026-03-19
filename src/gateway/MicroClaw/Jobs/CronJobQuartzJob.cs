using MicroClaw.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using Quartz;

namespace MicroClaw.Jobs;

/// <summary>Quartz 定时任务实现：触发时向目标 Session 发送预设提示词并获取 AI 回复。</summary>
[DisallowConcurrentExecution]
public sealed class CronJobQuartzJob(
    CronJobStore cronJobStore,
    SessionChatService sessionChatService,
    ILogger<CronJobQuartzJob> logger) : IJob
{
    public const string JobIdKey = "cronJobId";

    public async Task Execute(IJobExecutionContext context)
    {
        string? jobId = context.JobDetail.JobDataMap.GetString(JobIdKey);
        if (string.IsNullOrWhiteSpace(jobId))
        {
            logger.LogError("CronJobQuartzJob: missing JobId in JobDataMap.");
            return;
        }

        CronJob? job = cronJobStore.GetById(jobId);
        if (job is null)
        {
            logger.LogWarning("CronJobQuartzJob: job '{JobId}' not found in DB, skipping.", jobId);
            return;
        }

        if (!job.IsEnabled)
        {
            logger.LogDebug("CronJobQuartzJob: job '{JobId}' is disabled, skipping.", jobId);
            return;
        }

        logger.LogInformation("CronJobQuartzJob: executing job '{JobName}' ({JobId}), target session '{SessionId}'.",
            job.Name, jobId, job.TargetSessionId);

        try
        {
            await sessionChatService.ExecuteAsync(job.TargetSessionId, job.Prompt, context.CancellationToken);
            cronJobStore.UpdateLastRun(jobId, DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CronJobQuartzJob: error executing job '{JobId}'.", jobId);
        }
    }
}
