using System.Text;
using System.Text.RegularExpressions;

namespace MicroClaw.Agent.Memory;

/// <summary>
/// DNA 基因文件服务：三层架构（全局 / Agent / 会话），管理各层 dna/ 目录下的 Markdown 记忆文件。
/// 支持按 Category（子目录）组织，提供 CRUD、SystemPrompt 注入和版本快照。
/// </summary>
public sealed class DNAService(string agentsDataDir, string globalDnaDir, string sessionsDnaDir)
{
    /// <summary>单次注入 SystemPrompt 的 DNA 上下文最大字节数（50 KB，每层独立计算）。</summary>
    public const int MaxDnaSizeBytes = 50 * 1024;

    /// <summary>每个基因文件最多保留的历史快照数量。</summary>
    public const int MaxSnapshotsPerFile = 20;

    // ── 路径辅助 ──────────────────────────────────────────────────────────────

    private string AgentDir(string agentId) => Path.Combine(agentsDataDir, agentId, "dna");
    private string SessionDnaDir(string sessionId) => Path.Combine(sessionsDnaDir, sessionId, "dna");

    /// <summary>快照目录：dnaRoot/.snapshots/{category}_{fileName}/</summary>
    private static string GetSnapshotsDir(string dnaRoot, string category, string fileName)
    {
        string prefix = string.IsNullOrWhiteSpace(category) ? fileName : $"{category.Replace('/', '_')}_{fileName}";
        return Path.Combine(dnaRoot, ".snapshots", prefix);
    }

    private static string BuildFilePathInDir(string dnaRoot, string category, string fileName)
    {
        string dir = string.IsNullOrWhiteSpace(category) ? dnaRoot : Path.Combine(dnaRoot, category);
        return Path.Combine(dir, fileName);
    }

    // ── 通用 CRUD（供三层复用）────────────────────────────────────────────────

    private static IReadOnlyList<GeneFile> ListFilesInDir(string dnaRoot)
    {
        if (!Directory.Exists(dnaRoot)) return [];

        return Directory.EnumerateFiles(dnaRoot, "*.md", SearchOption.AllDirectories)
            .Where(filePath => !filePath.Contains(".snapshots"))
            .Select(filePath =>
            {
                string relative = Path.GetRelativePath(dnaRoot, filePath);
                string category = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;
                string fileName = Path.GetFileName(filePath);
                string content = File.ReadAllText(filePath);
                DateTimeOffset updatedAt = File.GetLastWriteTimeUtc(filePath);
                return new GeneFile(fileName, category, content, updatedAt);
            })
            .ToList()
            .AsReadOnly();
    }

    private static GeneFile? GetFileInDir(string dnaRoot, string category, string fileName)
    {
        string filePath = BuildFilePathInDir(dnaRoot, category, fileName);
        if (!File.Exists(filePath)) return null;

        string content = File.ReadAllText(filePath);
        DateTimeOffset updatedAt = File.GetLastWriteTimeUtc(filePath);
        return new GeneFile(fileName, category, content, updatedAt);
    }

    private static GeneFile WriteFileInDir(string dnaRoot, string category, string fileName, string content)
    {
        string filePath = BuildFilePathInDir(dnaRoot, category, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

        if (File.Exists(filePath))
            SaveSnapshotInDir(dnaRoot, category, fileName, File.ReadAllText(filePath));

        File.WriteAllText(filePath, content);
        DateTimeOffset updatedAt = File.GetLastWriteTimeUtc(filePath);
        return new GeneFile(fileName, category, content, updatedAt);
    }

    private static bool DeleteFileInDir(string dnaRoot, string category, string fileName)
    {
        string filePath = BuildFilePathInDir(dnaRoot, category, fileName);
        if (!File.Exists(filePath)) return false;
        File.Delete(filePath);
        return true;
    }

    /// <summary>将指定 dnaRoot 下的所有基因文件拼接为 Markdown 注入块，各层独立 50 KB 上限。</summary>
    private static string BuildContextSection(string dnaRoot, string sectionHeader)
    {
        IReadOnlyList<GeneFile> files = ListFilesInDir(dnaRoot);
        if (files.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine(sectionHeader);
        int included = 0;

        foreach (GeneFile gene in files)
        {
            string label = string.IsNullOrWhiteSpace(gene.Category)
                ? gene.FileName
                : $"{gene.Category}/{gene.FileName}";
            string section = $"### {label}\n{gene.Content}\n\n";

            if (Encoding.UTF8.GetByteCount(sb.ToString()) + Encoding.UTF8.GetByteCount(section) > MaxDnaSizeBytes)
                break;

            sb.Append(section);
            included++;
        }

        if (included < files.Count)
            sb.AppendLine($"\n> ⚠️ 因 DNA 大小超过 50 KB 上限，仅注入 {included}/{files.Count} 个基因文件，剩余文件已跳过。");

        return sb.ToString().TrimEnd();
    }

    // ── 通用版本快照（供三层复用）────────────────────────────────────────────

    private static IReadOnlyList<GeneFileSnapshot> ListSnapshotsInDir(string dnaRoot, string category, string fileName)
    {
        string dir = GetSnapshotsDir(dnaRoot, category, fileName);
        if (!Directory.Exists(dir)) return [];

        return Directory.EnumerateFiles(dir, "*.md")
            .Select(path =>
            {
                string snapshotId = Path.GetFileNameWithoutExtension(path);
                string content = File.ReadAllText(path);
                DateTimeOffset savedAt = ParseSnapshotId(snapshotId);
                return new GeneFileSnapshot(snapshotId, fileName, category, savedAt, content);
            })
            .OrderByDescending(s => s.SavedAt)
            .ToList()
            .AsReadOnly();
    }

    private static GeneFileSnapshot? GetSnapshotInDir(string dnaRoot, string category, string fileName, string snapshotId)
    {
        string dir = GetSnapshotsDir(dnaRoot, category, fileName);
        string path = Path.Combine(dir, $"{snapshotId}.md");
        if (!File.Exists(path)) return null;

        string content = File.ReadAllText(path);
        DateTimeOffset savedAt = ParseSnapshotId(snapshotId);
        return new GeneFileSnapshot(snapshotId, fileName, category, savedAt, content);
    }

    private static GeneFile RestoreSnapshotInDir(string dnaRoot, string category, string fileName, string snapshotId)
    {
        GeneFileSnapshot? snapshot = GetSnapshotInDir(dnaRoot, category, fileName, snapshotId);
        if (snapshot is null)
            throw new FileNotFoundException($"Snapshot '{snapshotId}' not found for {category}/{fileName}.");

        return WriteFileInDir(dnaRoot, category, fileName, snapshot.Content);
    }

    private static void SaveSnapshotInDir(string dnaRoot, string category, string fileName, string content)
    {
        string dir = GetSnapshotsDir(dnaRoot, category, fileName);
        Directory.CreateDirectory(dir);

        string snapshotId = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ");
        string path = Path.Combine(dir, $"{snapshotId}.md");
        File.WriteAllText(path, content);

        string[] all = Directory.GetFiles(dir, "*.md").OrderBy(f => f).ToArray();
        for (int i = 0; i < all.Length - MaxSnapshotsPerFile; i++)
            File.Delete(all[i]);
    }

    private static DateTimeOffset ParseSnapshotId(string snapshotId)
    {
        if (DateTimeOffset.TryParseExact(
            snapshotId, "yyyy-MM-ddTHH-mm-ssZ",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out DateTimeOffset result))
            return result;

        return DateTimeOffset.MinValue;
    }

    // ── Agent DNA（第二层，原有接口保持不变）─────────────────────────────────

    public IReadOnlyList<GeneFile> List(string agentId) => ListFilesInDir(AgentDir(agentId));

    public GeneFile? Get(string agentId, string category, string fileName) =>
        GetFileInDir(AgentDir(agentId), category, fileName);

    public GeneFile Write(string agentId, string category, string fileName, string content) =>
        WriteFileInDir(AgentDir(agentId), category, fileName, content);

    public bool Delete(string agentId, string category, string fileName) =>
        DeleteFileInDir(AgentDir(agentId), category, fileName);

    public IReadOnlyList<GeneFileSnapshot> ListSnapshots(string agentId, string category, string fileName) =>
        ListSnapshotsInDir(AgentDir(agentId), category, fileName);

    public GeneFileSnapshot? GetSnapshot(string agentId, string category, string fileName, string snapshotId) =>
        GetSnapshotInDir(AgentDir(agentId), category, fileName, snapshotId);

    public GeneFile RestoreSnapshot(string agentId, string category, string fileName, string snapshotId) =>
        RestoreSnapshotInDir(AgentDir(agentId), category, fileName, snapshotId);

    /// <summary>将 Agent DNA（第二层）拼接为注入块。</summary>
    public string BuildSystemPromptContext(string agentId) =>
        BuildContextSection(AgentDir(agentId), "## Agent DNA");

    // ── 全局 DNA（第一层）────────────────────────────────────────────────────

    public IReadOnlyList<GeneFile> ListGlobal() => ListFilesInDir(globalDnaDir);

    public GeneFile? GetGlobal(string category, string fileName) =>
        GetFileInDir(globalDnaDir, category, fileName);

    public GeneFile WriteGlobal(string category, string fileName, string content) =>
        WriteFileInDir(globalDnaDir, category, fileName, content);

    public bool DeleteGlobal(string category, string fileName) =>
        DeleteFileInDir(globalDnaDir, category, fileName);

    public IReadOnlyList<GeneFileSnapshot> ListGlobalSnapshots(string category, string fileName) =>
        ListSnapshotsInDir(globalDnaDir, category, fileName);

    public GeneFileSnapshot? GetGlobalSnapshot(string category, string fileName, string snapshotId) =>
        GetSnapshotInDir(globalDnaDir, category, fileName, snapshotId);

    public GeneFile RestoreGlobalSnapshot(string category, string fileName, string snapshotId) =>
        RestoreSnapshotInDir(globalDnaDir, category, fileName, snapshotId);

    /// <summary>将全局 DNA（第一层）拼接为注入块。</summary>
    public string BuildGlobalContext() =>
        BuildContextSection(globalDnaDir, "## 全局 DNA");

    // ── 会话 DNA（第三层）────────────────────────────────────────────────────

    public IReadOnlyList<GeneFile> ListSession(string sessionId) => ListFilesInDir(SessionDnaDir(sessionId));

    public GeneFile? GetSession(string sessionId, string category, string fileName) =>
        GetFileInDir(SessionDnaDir(sessionId), category, fileName);

    public GeneFile WriteSession(string sessionId, string category, string fileName, string content) =>
        WriteFileInDir(SessionDnaDir(sessionId), category, fileName, content);

    public bool DeleteSession(string sessionId, string category, string fileName) =>
        DeleteFileInDir(SessionDnaDir(sessionId), category, fileName);

    public IReadOnlyList<GeneFileSnapshot> ListSessionSnapshots(string sessionId, string category, string fileName) =>
        ListSnapshotsInDir(SessionDnaDir(sessionId), category, fileName);

    public GeneFileSnapshot? GetSessionSnapshot(string sessionId, string category, string fileName, string snapshotId) =>
        GetSnapshotInDir(SessionDnaDir(sessionId), category, fileName, snapshotId);

    public GeneFile RestoreSessionSnapshot(string sessionId, string category, string fileName, string snapshotId) =>
        RestoreSnapshotInDir(SessionDnaDir(sessionId), category, fileName, snapshotId);

    /// <summary>将会话 DNA（第三层）拼接为注入块。</summary>
    public string BuildSessionContext(string sessionId) =>
        BuildContextSection(SessionDnaDir(sessionId), "## 会话 DNA");

    /// <summary>删除指定会话的 DNA 目录（会话删除时同步调用）。</summary>
    public void DeleteSessionDnaDir(string sessionId)
    {
        string dir = SessionDnaDir(sessionId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    // ── 三层合并注入 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 按顺序拼接三层 DNA 上下文：全局 → Agent → 会话，任意层为空则跳过。
    /// 供 AgentRunner.BuildChatMessages 调用。
    /// </summary>
    public string BuildFullSystemPromptContext(string agentId, string? sessionId = null)
    {
        string global = BuildGlobalContext();
        string agent = BuildSystemPromptContext(agentId);
        string session = sessionId is not null ? BuildSessionContext(sessionId) : string.Empty;

        var parts = new[] { global, agent, session }.Where(s => !string.IsNullOrEmpty(s));
        return string.Join("\n\n", parts);
    }

    // ── 导出/导入 Markdown ────────────────────────────────────────────────────

    /// <summary>将指定 Agent 的所有 DNA 文件导出为统一 Markdown 格式（可直接保存为 .md 文件）。</summary>
    public string ExportToMarkdown(string agentId) =>
        BuildMarkdownExport("agent", agentId, List(agentId));

    /// <summary>将全局 DNA 文件导出为统一 Markdown 格式。</summary>
    public string ExportGlobalToMarkdown() =>
        BuildMarkdownExport("global", null, ListGlobal());

    /// <summary>从 Markdown 文本导入 DNA 文件到指定 Agent（现有同名文件将被覆盖并生成快照）。</summary>
    public IReadOnlyList<DnaImportEntryResult> ImportFromMarkdown(string agentId, string markdown) =>
        ImportFilesFromMarkdown(markdown, (cat, name, content) => Write(agentId, cat, name, content));

    /// <summary>从 Markdown 文本导入 DNA 文件到全局 DNA 目录。</summary>
    public IReadOnlyList<DnaImportEntryResult> ImportGlobalFromMarkdown(string markdown) =>
        ImportFilesFromMarkdown(markdown, (cat, name, content) => WriteGlobal(cat, name, content));

    private static string BuildMarkdownExport(string scope, string? scopeId, IReadOnlyList<GeneFile> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# MicroClaw DNA Export");
        string meta = scopeId is not null
            ? $"<!-- scope: {scope} | id: {scopeId} | exported: {DateTimeOffset.UtcNow:O} -->"
            : $"<!-- scope: {scope} | exported: {DateTimeOffset.UtcNow:O} -->";
        sb.AppendLine(meta);

        foreach (GeneFile file in files)
        {
            sb.AppendLine();
            string path = string.IsNullOrWhiteSpace(file.Category)
                ? file.FileName
                : $"{file.Category}/{file.FileName}";
            sb.AppendLine($"## {path}");
            sb.AppendLine();
            sb.AppendLine(file.Content);
            sb.AppendLine();
            sb.AppendLine("---");
        }

        return sb.ToString();
    }

    private static readonly Regex SectionHeaderRegex =
        new(@"(?m)^## (.+)$", RegexOptions.Compiled | RegexOptions.Multiline);

    private static IReadOnlyList<DnaImportEntryResult> ImportFilesFromMarkdown(
        string markdown,
        Action<string, string, string> writeAction)
    {
        var results = new List<DnaImportEntryResult>();
        MatchCollection matches = SectionHeaderRegex.Matches(markdown);

        for (int i = 0; i < matches.Count; i++)
        {
            string path = matches[i].Groups[1].Value.Trim();
            int contentStart = matches[i].Index + matches[i].Length;
            int contentEnd = i + 1 < matches.Count ? matches[i + 1].Index : markdown.Length;
            string rawContent = markdown[contentStart..contentEnd];

            // 去除首尾空行及尾部 --- 分隔符
            string content = rawContent.Trim().TrimEnd('-').Trim();

            // 解析 category/fileName
            string category = string.Empty;
            string fileName = path;
            int slashIdx = path.IndexOf('/');
            if (slashIdx > 0 && slashIdx < path.Length - 1)
            {
                category = path[..slashIdx];
                fileName = path[(slashIdx + 1)..];
            }

            // 路径净化（防止路径穿越）
            string safeCategory = string.Join("/",
                category.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries)
                        .Select(Path.GetFileName)
                        .Where(s => !string.IsNullOrWhiteSpace(s))!);
            string safeFileName = Path.GetFileName(fileName);

            if (string.IsNullOrWhiteSpace(safeFileName))
            {
                results.Add(new DnaImportEntryResult(path, string.Empty, false, "文件名不合法"));
                continue;
            }

            if (!safeFileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                safeFileName += ".md";

            try
            {
                writeAction(safeCategory, safeFileName, content);
                results.Add(new DnaImportEntryResult(safeFileName, safeCategory, true, null));
            }
            catch (Exception ex)
            {
                results.Add(new DnaImportEntryResult(safeFileName, safeCategory, false, ex.Message));
            }
        }

        return results;
    }
}
