using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Skills;

/// <summary>
/// 为 AgentRunner 批量创建技能 AIFunction 工具列表。
/// </summary>
public sealed class SkillToolFactory(
    SkillStore skillStore,
    SkillService skillService,
    SkillRunner skillRunner,
    string workspaceRoot,
    ILoggerFactory loggerFactory)
{
    /// <summary>
    /// 为指定技能 ID 列表创建 AIFunction 工具，只加载 IsEnabled 且非 Playbook 模式的技能。
    /// Playbook 模式（有 SKILL.md）的技能通过 BuildSkillSystemPromptFragment 注入 SystemPrompt。
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

            // Playbook 模式（有 SKILL.md）通过 SystemPrompt 注入，不注册为 AIFunction
            if (skillService.IsPlaybookMode(skill.Id)) continue;

            tools.Add(new SkillAIFunction(
                skill,
                skillService,
                skillRunner,
                workspaceRoot,
                sessionId,
                loggerFactory));
        }

        return tools.AsReadOnly();
    }

    /// <summary>
    /// 构建绑定技能中所有 Playbook 模式技能（有 SKILL.md）的系统提示片段。
    /// 脚本模式技能由 CreateTools() 处理为 AIFunction，不在此处包含。
    /// </summary>
    public string BuildSkillSystemPromptFragment(IReadOnlyList<string> boundSkillIds)
    {
        if (boundSkillIds.Count == 0) return string.Empty;

        var fragments = new List<string>();
        foreach (string id in boundSkillIds)
        {
            SkillConfig? skill = skillStore.GetById(id);
            if (skill is null || !skill.IsEnabled) continue;

            string? md = skillService.GetSkillMd(skill.Id);
            if (string.IsNullOrWhiteSpace(md)) continue;

            fragments.Add($"## Skill: {skill.Name}\n\n{md.Trim()}");
        }

        if (fragments.Count == 0) return string.Empty;
        return $"# Active Skills\n\n{string.Join("\n\n---\n\n", fragments)}";
    }
}
