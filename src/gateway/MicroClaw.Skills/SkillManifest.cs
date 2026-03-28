namespace MicroClaw.Skills;

/// <summary>
/// Agent Skills 开放标准（agentskills.io）SKILL.md frontmatter 解析结果。
/// 所有字段对应开放标准的 frontmatter key，从文件系统读取，不存储于数据库。
/// </summary>
public sealed record SkillManifest(
    /// <summary>技能显示名称（frontmatter: name）；省略时由目录名决定。</summary>
    string Name,
    /// <summary>技能描述，AI 据此决定何时自动加载（frontmatter: description）。</summary>
    string Description,
    /// <summary>true 时 AI 不会自动加载此技能，仅用户通过 /name 手动调用（frontmatter: disable-model-invocation）。</summary>
    bool DisableModelInvocation,
    /// <summary>false 时隐藏出 /菜单，仅 AI 自动调用（frontmatter: user-invocable，默认 true）。</summary>
    bool UserInvocable,
    /// <summary>调用时允许 AI 无需确认的工具列表，逗号分隔（frontmatter: allowed-tools）。</summary>
    string AllowedTools,
    /// <summary>技能活跃时使用的模型（frontmatter: model，可选）。</summary>
    string? Model,
    /// <summary>技能活跃时的推理强度（frontmatter: effort；low/medium/high/max，可选）。</summary>
    string? Effort,
    /// <summary>"fork" 时在隔离子 agent 中执行（frontmatter: context，可选）。</summary>
    string? Context,
    /// <summary>context: fork 时使用的子 agent 类型（frontmatter: agent；如 Explore/Plan，可选）。</summary>
    string? Agent,
    /// <summary>自动补全提示，如 "[query]"（frontmatter: argument-hint）。</summary>
    string ArgumentHint,
    /// <summary>技能可访问的路径列表（frontmatter: paths，逗号分隔，可选）。</summary>
    string? Paths,
    /// <summary>脚本执行使用的 shell（frontmatter: shell；如 bash/powershell/cmd，可选）。</summary>
    string? Shell,
    /// <summary>技能生命周期 hooks（frontmatter: hooks，可选）。</summary>
    string? Hooks,
    /// <summary>解析后的 hooks 结构（从 Hooks 字符串懒解析，供 SkillInvocationTool 使用）。</summary>
    SkillHooks ParsedHooks,
    /// <summary>frontmatter 之后的 Markdown 正文，即技能的指令内容。</summary>
    string Instructions)
{
    public static readonly SkillManifest Fallback = new(
        Name: string.Empty,
        Description: string.Empty,
        DisableModelInvocation: false,
        UserInvocable: true,
        AllowedTools: string.Empty,
        Model: null,
        Effort: null,
        Context: null,
        Agent: null,
        ArgumentHint: string.Empty,
        Paths: null,
        Shell: null,
        Hooks: null,
        ParsedHooks: SkillHooks.Empty,
        Instructions: string.Empty);

    /// <summary>
    /// 从 SKILL.md 文本解析 frontmatter，构建 SkillManifest。
    /// 若文本为空或无 frontmatter 块，返回仅含正文的 <see cref="Fallback"/>。
    /// 未知 key 静默忽略（向前兼容）。
    /// hooks: 支持多行缩进块收集。
    /// </summary>
    public static SkillManifest Parse(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return Fallback;

        ReadOnlySpan<char> span = content.AsSpan().TrimStart();
        if (!span.StartsWith("---"))
            return Fallback with { Instructions = content.Trim() };

        int firstEnd = content.IndexOf("---", 3, StringComparison.Ordinal);
        if (firstEnd < 0)
            return Fallback with { Instructions = content.Trim() };

        string frontmatterBlock = content[3..firstEnd];
        string instructions = content[(firstEnd + 3)..].TrimStart('\r', '\n').Trim();

        string name = string.Empty;
        string description = string.Empty;
        bool disableModelInvocation = false;
        bool userInvocable = true;
        string allowedTools = string.Empty;
        string? model = null;
        string? effort = null;
        string? context = null;
        string? agent = null;
        string argumentHint = string.Empty;
        string? paths = null;
        string? shell = null;
        string? hooks = null;
        var hooksLines = new List<string>();
        bool inHooksBlock = false;

        string[] lines = frontmatterBlock.Split('\n');
        foreach (string line in lines)
        {
            // hooks: 可能是单行值，也可能开始一个多行缩进块
            if (inHooksBlock)
            {
                // 以空白开头 = hooks 的子内容行
                if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
                {
                    hooksLines.Add(line.TrimStart());
                    continue;
                }
                // 未缩进 = hooks 块结束
                inHooksBlock = false;
            }

            int colon = line.IndexOf(':');
            if (colon < 0) continue;

            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();

            switch (key)
            {
                case "name":
                    name = value.Trim('"', '\'');
                    break;
                case "description":
                    description = value.Trim('"', '\'');
                    break;
                case "disable-model-invocation":
                    disableModelInvocation = value.Equals("true", StringComparison.OrdinalIgnoreCase);
                    break;
                case "user-invocable":
                    userInvocable = !value.Equals("false", StringComparison.OrdinalIgnoreCase);
                    break;
                case "allowed-tools":
                    allowedTools = value.Trim('"', '\'');
                    break;
                case "model":
                    model = value.Trim('"', '\'');
                    break;
                case "effort":
                    effort = value.ToLowerInvariant().Trim('"', '\'');
                    break;
                case "context":
                    context = value.ToLowerInvariant().Trim('"', '\'');
                    break;
                case "agent":
                    agent = value.Trim('"', '\'');
                    break;
                case "argument-hint":
                    argumentHint = value.Trim('"', '\'');
                    break;
                case "paths":
                    paths = value.Trim('"', '\'');
                    break;
                case "shell":
                    shell = value.Trim('"', '\'');
                    break;
                case "hooks":
                    // 单行值（如 hooks: "..."）或多行块开始（值为空）
                    if (!string.IsNullOrWhiteSpace(value))
                        hooks = value.Trim('"', '\'');
                    else
                        inHooksBlock = true;
                    break;
                // 旧版非标准字段（skill-type / entry-point / timeout）静默忽略
            }
        }

        // 从收集到的 hooks 子行直接解析结构化 hooks，避免 string.Join → Split 往返
        SkillHooks parsedHooks;
        if (hooksLines.Count > 0)
        {
            hooks = string.Join("\n", hooksLines);
            parsedHooks = SkillHooks.ParseLines(hooksLines);
        }
        else
        {
            parsedHooks = SkillHooks.Parse(hooks);
        }

        return new SkillManifest(name, description, disableModelInvocation, userInvocable,
            allowedTools, model, effort, context, agent, argumentHint, paths, shell, hooks, parsedHooks, instructions);
    }
}
