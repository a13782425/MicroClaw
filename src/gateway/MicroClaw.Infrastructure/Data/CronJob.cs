namespace MicroClaw.Infrastructure.Data;

/// <summary>定时任务领域模型。CronExpression 与 RunAtUtc 互斥，必须有且仅有一个有値。</summary>
public record CronJob(
    string Id,
    string Name,
    string? Description,
    /// <summary>Quartz cron 表达式（周期性任务），null 表示这是一次性任务。</summary>
    string? CronExpression,
    string TargetSessionId,
    string Prompt,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastRunAtUtc,
    /// <summary>一次性任务的预定触发时间（UTC），null 表示这是周期性任务。</summary>
    DateTimeOffset? RunAtUtc = null);
