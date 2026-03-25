namespace MicroClaw.Infrastructure.Data;

public sealed class AgentConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? BoundSkillIdsJson { get; set; }
    public string? EnabledMcpServerIdsJson { get; set; }
    public string? ToolGroupConfigsJson { get; set; }
    /// <summary>创建时间：相对于 TimeBase.BaseTime 的毫秒偏移。</summary>
    public long CreatedAtMs { get; set; }
    public bool IsDefault { get; set; }
    public int? ContextWindowMessages { get; set; }
}
