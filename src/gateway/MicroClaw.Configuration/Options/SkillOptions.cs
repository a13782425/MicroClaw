namespace MicroClaw.Configuration;

/// <summary>
/// Skills 模块配置选项，从配置文件 skills: 节点读取。
/// </summary>
[MicroClawYamlConfig("skills", FileName = "skills.yaml", IsWritable = true)]
public sealed class SkillOptions : IMicroClawConfigTemplate
{
    /// <summary>
    /// 是否允许在技能指令中执行 !`command` shell 命令注入。
    /// 默认 false（安全关闭）。
    /// 开启后，技能 SKILL.md 中的 !`command` 占位符将在服务端执行对应 shell 命令，
    /// 并将输出替换进指令文本后再发送给 Claude。
    /// 仅在完全信任技能来源时开启；生产环境建议保持关闭。
    /// </summary>
    [YamlMember(Alias = "allow_command_injection")]
    public bool AllowCommandInjection { get; set; } = false;

    /// <summary>
    /// 技能描述目录（catalog fragment）的最大字符数预算。
    /// 超出预算时将截断末尾条目并附加 "[truncated]" 提示。
    /// 默认 16000 字符；设为 0 表示不限制。
    /// </summary>
    [YamlMember(Alias = "catalog_char_budget")]
    public int CatalogCharBudget { get; set; } = 16_000;

    /// <summary>
    /// 默认技能文件夹路径（相对 workspaceRoot 或绝对路径）。
    /// 相对路径以 workspaceRoot 为基准；绝对路径直接使用。
    /// 默认值 "skills" 对应 {workspaceRoot}/skills/。
    /// 新技能（通过 AI 创建）将写入此文件夹。
    /// </summary>
    [YamlMember(Alias = "default_folder")]
    public string DefaultFolder { get; set; } = "skills";

    /// <summary>
    /// 附加技能文件夹路径列表（相对 workspaceRoot 或绝对路径）。
    /// 这些文件夹在扫描和查找时会一并考虑，但新技能不会写入这些文件夹。
    /// </summary>
    [YamlMember(Alias = "additional_folders")]
    public List<string> AdditionalFolders { get; set; } = [];

    public IMicroClawConfigOptions CreateDefaultTemplate() => new SkillOptions();
}
