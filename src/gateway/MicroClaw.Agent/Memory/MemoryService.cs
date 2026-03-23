namespace MicroClaw.Agent.Memory;

/// <summary>单个每日记忆文件信息。</summary>
public sealed record DailyMemoryInfo(
    string Date,
    string Content,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Session 记忆服务：管理长期记忆 (MEMORY.md) 和每日记忆 (memory/YYYY-MM-DD.md)。
/// 文件存储于 {sessionsDir}/{sessionId}/ 下。
/// </summary>
public sealed class MemoryService(string sessionsDir)
{
    // ── 常量 ─────────────────────────────────────────────────────────────────

    private const string LongTermFile = "MEMORY.md";
    private const string DailySubDir = "memory";

    // 权重衰减策略：最近 7 天全量，7-30 天仅首行，30 天以上忽略（已在 MEMORY.md 摘要）
    private const int FullWeightDays = 7;
    private const int PartialWeightDays = 30;

    // ── 路径辅助 ──────────────────────────────────────────────────────────────

    private string SessionDir(string sessionId) => Path.Combine(sessionsDir, sessionId);

    private string LongTermPath(string sessionId) =>
        Path.Combine(SessionDir(sessionId), LongTermFile);

    private string DailyDir(string sessionId) =>
        Path.Combine(SessionDir(sessionId), DailySubDir);

    private string DailyPath(string sessionId, string date) =>
        Path.Combine(DailyDir(sessionId), $"{date}.md");

    // ── 日期格式校验 ──────────────────────────────────────────────────────────

    /// <summary>校验日期字符串格式为 YYYY-MM-DD。</summary>
    public static bool IsValidDateFormat(string date) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None,
            out _);

    // ── 长期记忆 ─────────────────────────────────────────────────────────────

    /// <summary>读取长期记忆（MEMORY.md）；文件不存在时返回空字符串。</summary>
    public string GetLongTermMemory(string sessionId)
    {
        string path = LongTermPath(sessionId);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <summary>更新长期记忆（MEMORY.md）。</summary>
    public void UpdateLongTermMemory(string sessionId, string content)
    {
        string dir = SessionDir(sessionId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(LongTermPath(sessionId), content);
    }

    // ── 每日记忆 ─────────────────────────────────────────────────────────────

    /// <summary>读取指定日期的记忆；文件不存在返回 null，日期格式非法返回 null。</summary>
    public DailyMemoryInfo? GetDailyMemory(string sessionId, string date)
    {
        if (!IsValidDateFormat(date)) return null;

        string path = DailyPath(sessionId, date);
        if (!File.Exists(path)) return null;

        string content = File.ReadAllText(path);
        DateTimeOffset updatedAt = new(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        return new DailyMemoryInfo(date, content, updatedAt);
    }

    /// <summary>写入指定日期的记忆（幂等覆盖）。日期格式非法时抛出 ArgumentException。</summary>
    public void WriteDailyMemory(string sessionId, string date, string content)
    {
        if (!IsValidDateFormat(date))
            throw new ArgumentException($"Invalid date format: '{date}'. Expected YYYY-MM-DD.", nameof(date));

        string dir = DailyDir(sessionId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(DailyPath(sessionId, date), content);
    }

    /// <summary>列出所有每日记忆文件（按日期降序排列），返回日期字符串列表。</summary>
    public IReadOnlyList<string> ListDailyMemories(string sessionId)
    {
        string dir = DailyDir(sessionId);
        if (!Directory.Exists(dir)) return [];

        return Directory.GetFiles(dir, "????-??-??.md")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(IsValidDateFormat)
            .OrderByDescending(d => d)
            .ToList()
            .AsReadOnly();
    }

    // ── 上下文注入 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 构建注入 System Prompt 的记忆上下文。
    /// 权重策略：
    /// - MEMORY.md（长期）：始终包含
    /// - 最近 7 天：全量内容
    /// - 7-30 天：仅包含每日记忆的首行摘要
    /// - 30 天以上：忽略（已摘要到 MEMORY.md）
    /// </summary>
    public string BuildMemoryContext(string sessionId)
    {
        var parts = new List<string>();

        // 1. 长期记忆
        string longTerm = GetLongTermMemory(sessionId);
        if (!string.IsNullOrWhiteSpace(longTerm))
        {
            parts.Add($"## 长期记忆\n\n{longTerm.Trim()}");
        }

        // 2. 每日记忆（权重衰减）
        IReadOnlyList<string> dates = ListDailyMemories(sessionId);
        if (dates.Count == 0) return string.Join("\n\n", parts);

        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);
        var recentFull = new List<string>();
        var partialSummary = new List<string>();

        foreach (string date in dates)
        {
            if (!DateOnly.TryParseExact(date, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateOnly memDate))
                continue;

            int daysAgo = today.DayNumber - memDate.DayNumber;

            if (daysAgo < 0) continue; // 未来日期，跳过

            if (daysAgo <= FullWeightDays)
            {
                DailyMemoryInfo? info = GetDailyMemory(sessionId, date);
                if (info is not null && !string.IsNullOrWhiteSpace(info.Content))
                    recentFull.Add($"### {date}\n\n{info.Content.Trim()}");
            }
            else if (daysAgo <= PartialWeightDays)
            {
                DailyMemoryInfo? info = GetDailyMemory(sessionId, date);
                if (info is not null && !string.IsNullOrWhiteSpace(info.Content))
                {
                    string firstLine = info.Content.Trim().Split('\n')[0];
                    partialSummary.Add($"- {date}: {firstLine}");
                }
            }
            // 30+ 天：忽略
        }

        if (recentFull.Count > 0)
        {
            parts.Add("## 近期记忆（最近 7 天）\n\n" + string.Join("\n\n", recentFull));
        }

        if (partialSummary.Count > 0)
        {
            parts.Add("## 历史记忆摘要（7-30 天）\n\n" + string.Join("\n", partialSummary));
        }

        return string.Join("\n\n", parts);
    }
}
