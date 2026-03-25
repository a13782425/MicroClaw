namespace MicroClaw.Infrastructure.Data;

public sealed class SkillConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
