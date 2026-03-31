using System.ComponentModel;
using MicroClaw.Configuration;
using MicroClaw.Tools;
using Microsoft.Extensions.AI;

namespace MicroClaw.Skills;

/// <summary>
/// 技能调用工具提供者 — 包装 <see cref="SkillInvocationTool"/>，将其纳入统一 <see cref="IToolProvider"/> 体系。
/// 注册三个工具：invoke_skill（加载技能指令）、read_skill_file（读取附属文件）、run_skill_script（执行脚本）。
/// 默认启用所有技能，通过 <see cref="ToolCreationContext.DisabledSkillIds"/> 排除不需要的技能（opt-out 模型）。
/// </summary>
public sealed class SkillToolProvider(
    SkillInvocationTool skillInvocationTool,
    SkillService skillService,
    SkillStore skillStore) : IToolProvider
{
    /// <summary>技能内部工具名称集合（仅 LLM 可见，前端不推送）。与工具注册同源，单一事实来源。</summary>
    public static readonly HashSet<string> InternalToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "invoke_skill",
        "read_skill_file",
        "run_skill_script"
    };

    public ToolCategory Category => ToolCategory.Core;
    public string GroupId => "skill";
    public string DisplayName => "技能调用";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
    [
        ("invoke_skill", "调用已绑定的技能，加载其完整指令并按指令执行任务。"),
        ("read_skill_file", "读取技能目录中的附属文件内容（由 invoke_skill 返回的 availableFiles 列出）。"),
        ("run_skill_script", "在技能目录中执行脚本或命令（需启用 AllowCommandInjection）。")
    ];

    public Task<ToolProviderResult> CreateToolsAsync(ToolCreationContext context, CancellationToken ct = default)
    {
        // 全部技能减去排除列表（空排除列表 = 全部启用）
        IReadOnlyList<string> allSkillIds = skillStore.All;
        IReadOnlyList<string> effectiveIds = context.DisabledSkillIds is { Count: > 0 }
            ? allSkillIds.Where(id => !context.DisabledSkillIds.Contains(id)).ToList()
            : allSkillIds;

        if (effectiveIds.Count == 0)
            return Task.FromResult(ToolProviderResult.Empty);

        var tools = new List<AIFunction>
        {
            skillInvocationTool.Create(effectiveIds, context.SessionId),
            CreateReadSkillFile(effectiveIds),
            CreateRunSkillScript(effectiveIds)
        };

        return Task.FromResult(new ToolProviderResult(tools));
    }

    /// <summary>
    /// read_skill_file — 读取技能附属文件（Level 3 渐进加载）。
    /// invoke_skill 返回 availableFiles 列表后，AI 可按需读取具体文件内容。
    /// </summary>
    private AIFunction CreateReadSkillFile(IReadOnlyList<string> boundSkillIds)
    {
        return AIFunctionFactory.Create(
            (
                [Description("技能名称（与 invoke_skill 中使用的名称一致）")] string skillName,
                [Description("要读取的文件路径（来自 invoke_skill 返回的 availableFiles）")] string filePath
            ) =>
            {
                string? skillId = ResolveSkillId(boundSkillIds, skillName);
                if (skillId is null)
                    return new { success = false, error = $"技能 '{skillName}' 未找到或未启用。" };

                string? content = skillService.GetFile(skillId, filePath);
                if (content is null)
                    return (object)new { success = false, error = $"文件 '{filePath}' 不存在。" };

                return new { success = true, filePath, content };
            },
            name: "read_skill_file",
            description: "读取技能目录中的附属文件内容。文件路径来自 invoke_skill 返回的 availableFiles 列表。");
    }

    /// <summary>
    /// run_skill_script — 在技能目录中执行脚本/命令。
    /// 受 AllowCommandInjection 开关控制，关闭时拒绝执行。
    /// </summary>
    private AIFunction CreateRunSkillScript(IReadOnlyList<string> boundSkillIds)
    {
        return AIFunctionFactory.Create(
            (
                [Description("技能名称（与 invoke_skill 中使用的名称一致）")] string skillName,
                [Description("要执行的脚本文件路径（相对技能目录）或 shell 命令")] string command,
                [Description("超时秒数（默认 30）")] int timeoutSeconds
            ) =>
            {
                SkillOptions options = MicroClawConfig.Get<SkillOptions>();
                if (!options.AllowCommandInjection)
                    return (object)new { success = false, error = "脚本执行已禁用（AllowCommandInjection=false）。" };

                string? skillId = ResolveSkillId(boundSkillIds, skillName);
                if (skillId is null)
                    return new { success = false, error = $"技能 '{skillName}' 未找到或未启用。" };

                string workDir = skillService.GetSkillDirectory(skillId);
                int clampedTimeout = Math.Clamp(timeoutSeconds, 1, 120);
                string? shell = skillService.ParseManifest(skillId).Shell;
                CommandResult result = skillService.ExecuteCommand(command, workDir, clampedTimeout, shell);

                return new { success = result.ExitCode == 0, exitCode = result.ExitCode, stdout = result.Stdout, stderr = result.Stderr };
            },
            name: "run_skill_script",
            description: "在技能目录中执行脚本或命令。需要服务端启用 AllowCommandInjection 开关。");
    }

    /// <summary>从绑定技能列表中按名称 → ID 解析。</summary>
    private string? ResolveSkillId(IReadOnlyList<string> boundSkillIds, string skillName)
    {
        // 先尝试直接 ID 匹配
        if (boundSkillIds.Contains(skillName, StringComparer.OrdinalIgnoreCase))
            return skillName;

        // 再尝试通过 manifest name 匹配
        foreach (string id in boundSkillIds)
        {
            SkillManifest manifest = skillService.ParseManifest(id);
            if (manifest.Name.Equals(skillName, StringComparison.OrdinalIgnoreCase))
                return id;
        }

        return null;
    }
}
