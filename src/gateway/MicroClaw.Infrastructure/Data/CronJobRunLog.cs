namespace MicroClaw.Infrastructure.Data;

/// <summary>定时任务执行日志领域模型。</summary>
public record CronJobRunLog(
    string Id,
    string CronJobId,
    DateTimeOffset TriggeredAtUtc,
    /// <summary>执行结果：success / failed / cancelled</summary>
    string Status,
    /// <summary>执行耗时（毫秒）</summary>
    long DurationMs,
    string? ErrorMessage,
    /// <summary>触发来源：cron（自动）/ manual（手动）</summary>
    string Source);
