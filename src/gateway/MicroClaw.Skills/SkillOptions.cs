namespace MicroClaw.Skills;

/// <summary>
/// Skills 模块配置选项，从配置文件 skills: 节点读取。
/// </summary>
public sealed class SkillOptions
{
    /// <summary>
    /// 是否允许在技能指令中执行 !`command` shell 命令注入。
    /// 默认 false（安全关闭）。
    /// 开启后，技能 SKILL.md 中的 !`command` 占位符将在服务端执行对应 shell 命令，
    /// 并将输出替换进指令文本后再发送给 Claude。
    /// 仅在完全信任技能来源时开启；生产环境建议保持关闭。
    /// </summary>
    public bool AllowCommandInjection { get; set; } = false;

    /// <summary>
    /// 技能描述目录（catalog fragment）的最大字符数预算。
    /// 超出预算时将截断末尾条目并附加 "[truncated]" 提示。
    /// 默认 16000 字符；设为 0 表示不限制。
    /// </summary>
    public int CatalogCharBudget { get; set; } = 16_000;
}
