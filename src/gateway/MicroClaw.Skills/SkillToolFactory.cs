using Microsoft.Extensions.AI;

namespace MicroClaw.Skills;

/// <summary>
/// 为 AgentRunner 批量创建技能 AIFunction 工具列表。
/// </summary>
public sealed class SkillToolFactory(
    SkillStore skillStore,
    SkillService skillService,
    SkillRunner skillRunner,
    string workspaceRoot)
{
    /// <summary>
    /// 为指定技能 ID 列表创建 AIFunction 工具，只加载 IsEnabled 的技能。
    /// </summary>
    public IReadOnlyList<AIFunction> CreateTools(
        IReadOnlyList<string> skillIds,
        string sessionId)
    {
        if (skillIds.Count == 0) return [];

        var tools = new List<AIFunction>(skillIds.Count);
        foreach (string id in skillIds)
        {
            SkillConfig? skill = skillStore.GetById(id);
            if (skill is null || !skill.IsEnabled) continue;

            tools.Add(new SkillAIFunction(
                skill,
                skillService,
                skillRunner,
                workspaceRoot,
                sessionId));
        }

        return tools.AsReadOnly();
    }
}
