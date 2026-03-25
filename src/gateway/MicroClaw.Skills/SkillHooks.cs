namespace MicroClaw.Skills;

/// <summary>
/// 技能生命周期 hooks 集合，从 SKILL.md frontmatter 的 hooks: 块解析。
/// </summary>
public sealed record SkillHooks(
    IReadOnlyList<SkillHookEntry> OnInvoke,
    IReadOnlyList<SkillHookEntry> OnComplete)
{
    public static readonly SkillHooks Empty = new([], []);

    /// <summary>
    /// 从 hooks frontmatter 原始字符串解析。
    /// 支持的格式（YAML 子块）：
    /// <code>
    /// hooks:
    ///   on-invoke:
    ///     - type: command
    ///       command: echo invoked
    ///   on-complete:
    ///     - type: command
    ///       command: echo done
    /// </code>
    /// 提取方式为逐行手动解析，避免引入额外 YAML 依赖。
    /// </summary>
    public static SkillHooks Parse(string? rawBlock)
    {
        if (string.IsNullOrWhiteSpace(rawBlock))
            return Empty;

        string[] lines = rawBlock.Split('\n');
        return ParseLines(lines);
    }

    /// <summary>
    /// 从已拆分的行列表解析，避免 string.Join → Split 往返。
    /// 由 SkillManifest.Parse 在收集 hooks 子行后直接调用。
    /// </summary>
    internal static SkillHooks ParseLines(IReadOnlyList<string> lines)
    {
        if (lines.Count == 0)
            return Empty;

        var onInvoke = new List<SkillHookEntry>();
        var onComplete = new List<SkillHookEntry>();

        string currentSection = string.Empty;
        string? pendingType = null;
        string? pendingCommand = null;
        int pendingTimeout = 30;
        bool pendingFailOnError = false;

        foreach (string rawLine in lines)
        {
            string line = rawLine.TrimStart();

            // 区段标识符
            if (line.StartsWith("on-invoke:", StringComparison.OrdinalIgnoreCase))
            {
                FlushEntry(ref pendingType, ref pendingCommand, ref pendingTimeout, ref pendingFailOnError, currentSection, onInvoke, onComplete);
                currentSection = "on-invoke";
                continue;
            }
            if (line.StartsWith("on-complete:", StringComparison.OrdinalIgnoreCase))
            {
                FlushEntry(ref pendingType, ref pendingCommand, ref pendingTimeout, ref pendingFailOnError, currentSection, onInvoke, onComplete);
                currentSection = "on-complete";
                continue;
            }

            if (string.IsNullOrWhiteSpace(currentSection)) continue;

            // 列表项开始
            if (line.StartsWith("- type:", StringComparison.OrdinalIgnoreCase))
            {
                FlushEntry(ref pendingType, ref pendingCommand, ref pendingTimeout, ref pendingFailOnError, currentSection, onInvoke, onComplete);
                pendingType = line[7..].Trim().Trim('"', '\'');
                continue;
            }

            // type: 独立行（- 与 type: 分开写）
            if (line.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
            {
                if (pendingType is null)
                    pendingType = line[5..].Trim().Trim('"', '\'');
                continue;
            }

            // command: 值
            if (line.StartsWith("command:", StringComparison.OrdinalIgnoreCase))
            {
                pendingCommand = line[8..].Trim().Trim('"', '\'');
                continue;
            }

            // timeout: 值（秒）
            if (line.StartsWith("timeout:", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(line[8..].Trim(), out int t) && t > 0)
                    pendingTimeout = t;
                continue;
            }

            // fail-on-error: true/false
            if (line.StartsWith("fail-on-error:", StringComparison.OrdinalIgnoreCase))
            {
                pendingFailOnError = string.Equals(line[14..].Trim(), "true", StringComparison.OrdinalIgnoreCase);
                continue;
            }
        }

        FlushEntry(ref pendingType, ref pendingCommand, ref pendingTimeout, ref pendingFailOnError, currentSection, onInvoke, onComplete);

        return new SkillHooks(onInvoke.AsReadOnly(), onComplete.AsReadOnly());
    }

    private static void FlushEntry(
        ref string? type,
        ref string? command,
        ref int timeout,
        ref bool failOnError,
        string section,
        List<SkillHookEntry> onInvoke,
        List<SkillHookEntry> onComplete)
    {
        if (type is null || command is null) return;

        var entry = new SkillHookEntry(type, command, timeout, failOnError);
        if (section == "on-invoke") onInvoke.Add(entry);
        else if (section == "on-complete") onComplete.Add(entry);

        type = null;
        command = null;
        timeout = 30;
        failOnError = false;
    }
}

/// <summary>单条 hook 条目（type=command 时在调用前/后执行 shell 命令）。</summary>
public sealed record SkillHookEntry(
    string Type,
    string Command,
    int TimeoutSeconds = 30,
    bool FailOnError = false);
