namespace MicroClaw.Infrastructure.Data;

public sealed class CronJobEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string CronExpression { get; set; } = string.Empty;
    public string TargetSessionId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string CreatedAtUtc { get; set; } = string.Empty;
    public string? LastRunAtUtc { get; set; }
}
