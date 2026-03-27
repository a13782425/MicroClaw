using MicroClaw.Configuration;
using Microsoft.Extensions.Options;

namespace MicroClaw.Skills;

/// <summary>
/// 为 AgentRunner 构建技能上下文（描述目录 + 运行时覆盖），并为 SkillInvocationTool 按需提供全文指令。
/// 遵循 Agent Skills 开放标准（agentskills.io）懒加载模型：描述常驻 System Prompt，全文仅在调用时加载。
/// 支持官方字符串替换规范：$ARGUMENTS、$ARGUMENTS[N]、$N、${CLAUDE_SESSION_ID}、${CLAUDE_SKILL_DIR}。
/// </summary>
public sealed class SkillToolFactory(
    SkillStore skillStore,
    SkillService skillService,
    IOptions<SkillOptions> options)
{
    private readonly SkillOptions _options = options.Value;
    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// 构建技能上下文：仅包含描述目录片段（非全文），以及从 frontmatter 提取的运行时覆盖。
    /// DisableModelInvocation = true 的技能不进入目录（不暴露给 Claude 自动调用）。
    /// 全文指令仅在 invoke_skill 工具被调用时通过 <see cref="BuildSkillInstructions"/> 加载。
    /// </summary>
    public SkillContext BuildSkillContext(
        IReadOnlyList<string> boundSkillIds,
        string? sessionId = null)
    {
        if (boundSkillIds.Count == 0) return SkillContext.Empty;

        string? modelOverride = null;
        string? effortOverride = null;
        var approvedTools = new List<string>();
        var catalogEntries = new List<string>();

        foreach (string id in boundSkillIds)
        {
            SkillConfig? skill = skillStore.GetById(id);
            if (skill is null) continue;

            SkillManifest manifest = skillService.ParseManifest(id);
            if (manifest.DisableModelInvocation) continue;

            // 收集第一个有效的 model/effort 覆盖（优先级最高的技能）
            if (modelOverride is null && !string.IsNullOrWhiteSpace(manifest.Model))
                modelOverride = manifest.Model;
            if (effortOverride is null && !string.IsNullOrWhiteSpace(manifest.Effort))
                effortOverride = manifest.Effort;

            // 合并所有 allowed-tools
            if (!string.IsNullOrWhiteSpace(manifest.AllowedTools))
            {
                foreach (string tool in manifest.AllowedTools.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    approvedTools.Add(tool);
            }

            // 构建目录条目（仅名称+描述，不含全文）
            string slashName = !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : id;
            string description = manifest.Description;
            string hint = string.IsNullOrWhiteSpace(manifest.ArgumentHint) ? string.Empty : $" {manifest.ArgumentHint}";
            catalogEntries.Add($"- `/{slashName}{hint}`: {description}");
        }

        string catalogFragment = catalogEntries.Count == 0
            ? string.Empty
            : BuildCatalogFragment(catalogEntries);

        return new SkillContext(
            CatalogFragment: catalogFragment,
            ModelOverride: modelOverride,
            EffortOverride: effortOverride,
            AutoApprovedTools: approvedTools.AsReadOnly());
    }

    private string BuildCatalogFragment(List<string> entries)
    {
        const string header = "# Available Skills\n\nInvoke skills using the `invoke_skill` tool when a user's request matches a skill description.\n\n";
        int budget = _options.CatalogCharBudget;

        if (budget <= 0)
            return $"{header}{string.Join("\n", entries)}";

        int remaining = budget - header.Length;
        var included = new List<string>();
        foreach (string entry in entries)
        {
            int cost = entry.Length + 1; // +1 for newline
            if (remaining < cost)
            {
                included.Add("[... truncated — catalog budget exceeded]");
                break;
            }
            included.Add(entry);
            remaining -= cost;
        }

        return $"{header}{string.Join("\n", included)}";
    }

    /// <summary>
    /// 按技能名称（manifest.Name 或 entity.Name）在绑定列表中查找技能，并返回渲染后的完整指令。
    /// 由 SkillInvocationTool 在实际调用时使用，实现懒加载。
    /// </summary>
    /// <returns>渲染后的指令文本；若技能不存在或未启用则返回 null。</returns>
    public string? BuildSkillInstructions(
        IReadOnlyList<string> boundSkillIds,
        string skillName,
        string? sessionId = null,
        string? arguments = null)
    {
        var resolved = ResolveSkill(boundSkillIds, skillName);
        if (resolved is null) return null;

        var (config, manifest) = resolved.Value;
        return BuildSkillInstructionsFromManifest(config!.Id, manifest, sessionId, arguments);
    }

    /// <summary>
    /// 基于已解析的 manifest 渲染完整指令文本（!command 注入 + $-替换）。
    /// 供 SkillInvocationTool 在 ResolveSkill 之后直接调用，避免重复解析 SKILL.md。
    /// </summary>
    /// <returns>渲染后的指令文本；若指令为空则返回 empty。</returns>
    public string BuildSkillInstructionsFromManifest(
        string skillId,
        SkillManifest manifest,
        string? sessionId = null,
        string? arguments = null)
    {
        if (string.IsNullOrWhiteSpace(manifest.Instructions)) return string.Empty;

        string skillDir = skillService.GetSkillDirectory(skillId);
        string text = manifest.Instructions.Trim();

        // !`command` 注入（在 $-替换之前执行，受 AllowCommandInjection 开关控制）
        if (_options.AllowCommandInjection)
            text = skillService.ApplyCommandInjections(text, skillDir);

        return ApplySubstitutions(text, skillDir, sessionId, arguments);
    }

    /// <summary>
    /// 返回指定技能的 manifest，供 SkillInvocationTool 读取 context/agent 等元数据。
    /// </summary>
    public (SkillConfig? Config, SkillManifest Manifest)? ResolveSkill(
        IReadOnlyList<string> boundSkillIds,
        string skillName)
    {
        foreach (string id in boundSkillIds)
        {
            SkillConfig? skill = skillStore.GetById(id);
            if (skill is null) continue;

            SkillManifest manifest = skillService.ParseManifest(id);
            string resolvedName = !string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Name : id;
            if (!string.Equals(resolvedName, skillName, StringComparison.OrdinalIgnoreCase))
                continue;

            return (skill, manifest);
        }

        return null;
    }

    // ── Internal（供 SkillService 调用）──────────────────────────────────────

    /// <summary>
    /// 按官方规范依次替换技能指令中的字符串占位符。
    /// 替换顺序：位置参数 → 全量参数 → 会话变量 → 目录变量。
    /// </summary>
    internal static string ApplySubstitutions(string text, string skillDir, string? sessionId, string? arguments)
    {
        // 预拆分参数列表（按空白字符分割）
        string[] argParts = string.IsNullOrWhiteSpace(arguments)
            ? []
            : arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // 1. $ARGUMENTS[N] — 按索引取参数
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\$ARGUMENTS\[(\d+)\]",
            m =>
            {
                int idx = int.Parse(m.Groups[1].Value);
                return idx < argParts.Length ? argParts[idx] : string.Empty;
            });

        // 2. $N shorthand — $0、$1 等，仅在词边界替换，避免误替换 $ARGUMENTS
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\$(\d+)\b",
            m =>
            {
                int idx = int.Parse(m.Groups[1].Value);
                return idx < argParts.Length ? argParts[idx] : string.Empty;
            });

        // 3. $ARGUMENTS — 全量参数字符串
        string allArgs = arguments ?? string.Empty;
        if (text.Contains("$ARGUMENTS"))
        {
            text = text.Replace("$ARGUMENTS", allArgs);
        }
        else if (!string.IsNullOrWhiteSpace(arguments))
        {
            // 官方规范：$ARGUMENTS 不在内容中但有参数时，末尾追加
            text = $"{text}\n\nARGUMENTS: {allArgs}";
        }

        // 4. ${CLAUDE_SESSION_ID}
        text = text.Replace("${CLAUDE_SESSION_ID}", sessionId ?? string.Empty);

        // 5. ${CLAUDE_SKILL_DIR}
        text = text.Replace("${CLAUDE_SKILL_DIR}", skillDir);

        return text;
    }
}

