namespace MicroClaw.Skills;

/// <summary>
/// Skill 技能配置数据模型。
/// </summary>
public sealed record SkillConfig(
    string Id,
    string Name,
    string Description,
    /// <summary>执行类型：python / nodejs / shell</summary>
    string SkillType,
    /// <summary>入口脚本文件名（相对于 workspace/skills/{id}/ 目录）</summary>
    string EntryPoint,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc);
