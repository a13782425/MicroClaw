namespace MicroClaw.Infrastructure.Data;

/// <summary>定时任务领域模型。</summary>
public record CronJob(
    string Id,
    string Name,
    string? Description,
    string CronExpression,
    string TargetSessionId,
    string Prompt,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastRunAtUtc);
