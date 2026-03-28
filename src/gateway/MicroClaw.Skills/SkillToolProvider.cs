using MicroClaw.Tools;
using Microsoft.Extensions.AI;

namespace MicroClaw.Skills;

/// <summary>
/// 技能调用工具提供者 — 包装 <see cref="SkillInvocationTool"/>，将其纳入统一 <see cref="IToolProvider"/> 体系。
/// 当 Agent 绑定了技能（<see cref="ToolCreationContext.BoundSkillIds"/> 非空）时注入 invoke_skill 工具。
/// </summary>
public sealed class SkillToolProvider(SkillInvocationTool skillInvocationTool) : IToolProvider
{
    public ToolCategory Category => ToolCategory.Builtin;
    public string GroupId => "skill";
    public string DisplayName => "技能调用";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        [("invoke_skill", "调用已绑定的技能，加载其完整指令并按指令执行任务。")];

    public Task<ToolProviderResult> CreateToolsAsync(ToolCreationContext context, CancellationToken ct = default)
    {
        if (context.BoundSkillIds is null || context.BoundSkillIds.Count == 0)
            return Task.FromResult(ToolProviderResult.Empty);

        AIFunction tool = skillInvocationTool.Create(context.BoundSkillIds, context.SessionId);
        return Task.FromResult(new ToolProviderResult([tool]));
    }
}
