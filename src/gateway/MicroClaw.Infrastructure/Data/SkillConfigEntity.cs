namespace MicroClaw.Infrastructure.Data;

public sealed class SkillConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>技能执行类型：python / nodejs / shell</summary>
    public string SkillType { get; set; } = string.Empty;
    /// <summary>技能入口脚本文件名（相对于 workspace/skills/{id}/ 目录）</summary>
    public string EntryPoint { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
    /// <summary>执行超时秒数（默认 30 秒）</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
