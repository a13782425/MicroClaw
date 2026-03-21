namespace MicroClaw.Infrastructure.Data;

public sealed class AgentConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SystemPrompt { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? BoundSkillIdsJson { get; set; }
    public string? McpServersJson { get; set; }
    public string? ToolGroupConfigsJson { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public bool IsDefault { get; set; }
    public int? ContextWindowMessages { get; set; }
}
