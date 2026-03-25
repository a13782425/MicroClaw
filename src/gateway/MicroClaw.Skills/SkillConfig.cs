namespace MicroClaw.Skills;

/// <summary>
/// Skill 技能管控元数据（数据库存储）。
/// name/description 等运行时配置统一从 SKILL.md frontmatter 实时读取，不再双写。
/// Id = 目录名 slug（小写字母+数字+连字符，max 64）。
/// </summary>
public sealed record SkillConfig(
    string Id,
    bool IsEnabled,
    DateTimeOffset CreatedAtUtc);
