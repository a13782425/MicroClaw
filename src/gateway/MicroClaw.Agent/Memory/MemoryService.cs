using System.Text.Json;
using MicroClaw.Gateway.Contracts.Sessions;

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
    private const string CategoriesFile = "categories.json";

    // 权重衰减策略：最近 3 天全量，3 天以上由 RAG 分类 chunk 提供
    private const int FullWeightDays = 3;

    // ── 路径辅助 ──────────────────────────────────────────────────────────────

    private string SessionDir(string sessionId) => Path.Combine(sessionsDir, sessionId);

    private string LongTermPath(string sessionId) =>
        Path.Combine(SessionDir(sessionId), LongTermFile);

    private string DailyDir(string sessionId) =>
        Path.Combine(SessionDir(sessionId), DailySubDir);

    private string DailyPath(string sessionId, string date) =>
        Path.Combine(DailyDir(sessionId), $"{date}.md");

    private string CategoriesPath(string sessionId) =>
        Path.Combine(DailyDir(sessionId), CategoriesFile);

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

    /// <summary>删除指定日期的每日记忆文件。文件不存在时静默返回 false。</summary>
    public bool DeleteDailyMemory(string sessionId, string date)
    {
        if (!IsValidDateFormat(date)) return false;

        string path = DailyPath(sessionId, date);
        if (!File.Exists(path)) return false;

        File.Delete(path);
        return true;
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

    // ── 分类记忆 ──────────────────────────────────────────────────────────────

    /// <summary>读取分类记忆 JSON（memory/categories.json）；文件不存在时返回 "{}"。</summary>
    public string GetCategoriesJson(string sessionId)
    {
        string path = CategoriesPath(sessionId);
        return File.Exists(path) ? File.ReadAllText(path) : "{}";
    }

    /// <summary>写入分类记忆 JSON（memory/categories.json）。</summary>
    public void WriteCategoriesJson(string sessionId, string json)
    {
        string dir = DailyDir(sessionId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(CategoriesPath(sessionId), json);
    }

    // ── 上下文注入 ───────────────────────────────────────────────────────────

    /// <summary>
    /// 构建注入 System Prompt 的记忆上下文。
    /// 只注入 MEMORY.md（长期分类目录），近期原文已移除以减少 token 消耗，
    /// 细节由 RAG 分类 chunk 按需检索提供。
    /// </summary>
    public string BuildMemoryContext(string sessionId)
    {
        string longTerm = GetLongTermMemory(sessionId);
        if (string.IsNullOrWhiteSpace(longTerm)) return string.Empty;
        return $"## 长期记忆\n\n{longTerm.Trim()}";
    }

    // ── 待归纳消息 (pending) ──────────────────────────────────────────────────

    private const string PendingSubDir = "pending";

    private static readonly JsonSerializerOptions PendingJsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private string PendingDir(string sessionId) =>
        Path.Combine(DailyDir(sessionId), PendingSubDir);

    /// <summary>
    /// 将溢出消息序列化写入 pending 文件（memory/pending/时间戳-尾部ID.jsonl）。
    /// 返回写入的文件名。
    /// </summary>
    public string WritePendingMessages(string sessionId, IReadOnlyList<SessionMessage> messages)
    {
        string dir = PendingDir(sessionId);
        Directory.CreateDirectory(dir);

        string ts = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        string lastId = messages[^1].Id;
        string safeId = new string(lastId.Where(char.IsLetterOrDigit).ToArray())[..Math.Min(8, lastId.Length)];
        string fileName = $"{ts}-{safeId}.jsonl";

        IEnumerable<string> lines = messages.Select(m => JsonSerializer.Serialize(m, PendingJsonOpts));
        File.WriteAllText(Path.Combine(dir, fileName), string.Join("\n", lines));
        return fileName;
    }

    /// <summary>列出当前 session 的所有 pending 文件名（按时间戳升序）。</summary>
    public IReadOnlyList<string> ListPendingFiles(string sessionId)
    {
        string dir = PendingDir(sessionId);
        if (!Directory.Exists(dir)) return [];
        return Directory.GetFiles(dir, "*.jsonl")
            .Select(Path.GetFileName)
            .OfType<string>()
            .OrderBy(f => f)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>读取指定 pending 文件中的消息列表。文件不存在时返回空列表。</summary>
    public IReadOnlyList<SessionMessage> ReadPendingMessages(string sessionId, string fileName)
    {
        string path = Path.Combine(PendingDir(sessionId), fileName);
        if (!File.Exists(path)) return [];
        return File.ReadLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<SessionMessage>(l, PendingJsonOpts))
            .OfType<SessionMessage>()
            .ToList()
            .AsReadOnly();
    }

    /// <summary>删除指定 pending 文件。文件不存在时静默返回 false。</summary>
    public bool DeletePendingFile(string sessionId, string fileName)
    {
        string path = Path.Combine(PendingDir(sessionId), fileName);
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }
}
