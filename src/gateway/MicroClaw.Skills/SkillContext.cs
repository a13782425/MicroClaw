namespace MicroClaw.Skills;

/// <summary>
/// SkillToolFactory.BuildSkillContext() 的富结果类型。
/// 包含注入系统提示所需的描述目录片段，以及从 SKILL.md frontmatter 提取的运行时覆盖值。
/// </summary>
public sealed record SkillContext(
    /// <summary>
    /// 注入系统提示的技能描述目录（描述 + slash 名称，不含全文指令）。
    /// 告知 Claude 当前有哪些技能可用，以及何时通过 invoke_skill 工具调用它们。
    /// </summary>
    string CatalogFragment,

    /// <summary>
    /// 第一个绑定技能中非空的 model 覆盖值。
    /// 非空时 AgentRunner 将用此值覆盖 ChatOptions.ModelId。
    /// </summary>
    string? ModelOverride,

    /// <summary>
    /// 第一个绑定技能中非空的 effort 覆盖值（low/medium/high/max）。
    /// 非空时 AgentRunner 将用此值覆盖会话推理强度。
    /// </summary>
    string? EffortOverride,

    /// <summary>
    /// 所有绑定技能的 allowed-tools 合并列表（技能激活时 Claude 可免审批使用的工具名）。
    /// context:fork 时传给子 Agent 的工具过滤。
    /// </summary>
    IReadOnlyList<string> AutoApprovedTools)
{
    public static readonly SkillContext Empty = new(
        CatalogFragment: string.Empty,
        ModelOverride: null,
        EffortOverride: null,
        AutoApprovedTools: []);
}
