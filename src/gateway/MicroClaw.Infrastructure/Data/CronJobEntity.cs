namespace MicroClaw.Infrastructure.Data;

public sealed class CronJobEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    /// <summary>Quartz cron 表达式（周期性任务），与 RunAtUtc 互斥。</summary>
    public string? CronExpression { get; set; }
    /// <summary>一次性任务的预定触发时间（UTC），格式：ISO 8601，与 CronExpression 互斥。</summary>
    public string? RunAtUtc { get; set; }
    public string TargetSessionId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    /// <summary>创建时间（UTC），格式：ISO 8601 (DateTimeOffset.ToString("O"))</summary>
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string? LastRunAtUtc { get; set; }
}
