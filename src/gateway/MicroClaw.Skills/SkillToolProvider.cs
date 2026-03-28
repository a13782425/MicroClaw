using MicroClaw.Tools;
using Microsoft.Extensions.AI;

namespace MicroClaw.Skills;

/// <summary>
/// 技能调用工具提供者 — 包装 <see cref="SkillInvocationTool"/>，将其纳入统一 <see cref="IToolProvider"/> 体系。
/// 默认启用所有技能，通过 <see cref="ToolCreationContext.DisabledSkillIds"/> 排除不需要的技能（opt-out 模型）。
/// </summary>
public sealed class SkillToolProvider(SkillInvocationTool skillInvocationTool, SkillStore skillStore) : IToolProvider
{
    public ToolCategory Category => ToolCategory.Builtin;
    public string GroupId => "skill";
    public string DisplayName => "技能调用";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        [("invoke_skill", "调用已绑定的技能，加载其完整指令并按指令执行任务。")];

    public Task<ToolProviderResult> CreateToolsAsync(ToolCreationContext context, CancellationToken ct = default)
    {
        // 全部技能减去排除列表（空排除列表 = 全部启用）
        IReadOnlyList<string> allSkillIds = skillStore.All.Select(s => s.Id).ToList();
        IReadOnlyList<string> effectiveIds = context.DisabledSkillIds is { Count: > 0 }
            ? allSkillIds.Where(id => !context.DisabledSkillIds.Contains(id)).ToList()
            : allSkillIds;

        if (effectiveIds.Count == 0)
            return Task.FromResult(ToolProviderResult.Empty);

        AIFunction tool = skillInvocationTool.Create(effectiveIds, context.SessionId);
        return Task.FromResult(new ToolProviderResult([tool]));
    }
}
