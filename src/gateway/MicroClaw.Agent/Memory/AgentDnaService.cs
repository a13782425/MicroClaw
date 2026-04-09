using MicroClaw.Configuration;

namespace MicroClaw.Agent.Memory;

/// <summary>Agent DNA 文件信息。</summary>
public sealed record AgentDnaFileInfo(
    string FileName,
    string Description,
    string Content,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Agent 级 DNA 文件服务：每个 Agent 拥有两个固定文件（SOUL / MEMORY），
/// 存储于 {agentsDir}/{agentId}/ 下。
/// SOUL.md 定义 Agent 人格（跨所有会话共享），
/// MEMORY.md 存储 Agent 跨会话积累的长期经验（注入 SystemPrompt 时截取前 200 行）。
/// </summary>
public sealed class AgentDnaService
{
    private readonly string agentsDir = MicroClawConfig.Env.AgentsDir;

    // ── 常量 ─────────────────────────────────────────────────────────────────

    public const string SoulFile = "SOUL.md";
    public const string MemoryFile = "MEMORY.md";
    private const int MemoryMaxLines = 200;

    public static readonly IReadOnlyList<string> FixedFileNames = [SoulFile, MemoryFile];

    private static readonly IReadOnlyDictionary<string, string> FileDescriptions =
        new Dictionary<string, string>
        {
            [SoulFile] = "定义 Agent 的人格、语气和表达风格（跨所有会话共享）",
            [MemoryFile] = "Agent 跨会话积累的长期经验和洞察",
        };

    private static readonly IReadOnlyDictionary<string, string> DefaultTemplates =
        new Dictionary<string, string>
        {
            [SoulFile] = """
                # Soul — AI 人格与风格

                在此描述 AI 的人格特质、语气和表达风格。

                例如：
                - 语气：专业、友好、简洁
                - 表达风格：使用结构化列表；避免过于学术的措辞
                - 禁忌：不主动推销；不使用感叹号
                """,

            [MemoryFile] = string.Empty,
        };

    // ── 路径辅助 ──────────────────────────────────────────────────────────────

    private string AgentDir(string agentId) => Path.Combine(agentsDir, agentId);

    private string FilePath(string agentId, string fileName) =>
        Path.Combine(AgentDir(agentId), fileName);

    // ── 校验 ─────────────────────────────────────────────────────────────────

    /// <summary>检查文件名是否属于允许的两个固定文件之一。</summary>
    public static bool IsAllowedFileName(string fileName) =>
        FileDescriptions.ContainsKey(fileName);

    // ── 初始化 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 新建 Agent 时自动初始化 DNA 文件（幂等，已存在则跳过）。
    /// SOUL.md 写入默认模板，MEMORY.md 不创建空文件。
    /// </summary>
    public void InitializeAgent(string agentId)
    {
        string dir = AgentDir(agentId);
        Directory.CreateDirectory(dir);

        string soulPath = FilePath(agentId, SoulFile);
        if (!File.Exists(soulPath))
            File.WriteAllText(soulPath, DefaultTemplates[SoulFile]);
    }

    // ── SOUL ─────────────────────────────────────────────────────────────────

    /// <summary>读取 Agent 的 SOUL.md；文件不存在时返回空字符串。</summary>
    public string GetSoul(string agentId)
    {
        string path = FilePath(agentId, SoulFile);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <summary>更新 Agent 的 SOUL.md。</summary>
    public void UpdateSoul(string agentId, string content)
    {
        string dir = AgentDir(agentId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath(agentId, SoulFile), content);
    }

    // ── MEMORY ───────────────────────────────────────────────────────────────

    /// <summary>读取 Agent 的 MEMORY.md 全文；文件不存在时返回空字符串。</summary>
    public string GetMemory(string agentId)
    {
        string path = FilePath(agentId, MemoryFile);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    /// <summary>更新 Agent 的 MEMORY.md。</summary>
    public void UpdateMemory(string agentId, string content)
    {
        string dir = AgentDir(agentId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath(agentId, MemoryFile), content);
    }

    /// <summary>追加内容到 Agent 的 MEMORY.md 末尾。</summary>
    public void AppendMemory(string agentId, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        string existing = GetMemory(agentId);
        string newContent = string.IsNullOrWhiteSpace(existing)
            ? content.Trim()
            : existing.TrimEnd() + "\n\n" + content.Trim();
        UpdateMemory(agentId, newContent);
    }

    // ── 读取（通用） ─────────────────────────────────────────────────────────

    /// <summary>列出两个固定 DNA 文件（包含内容和元数据）。</summary>
    public IReadOnlyList<AgentDnaFileInfo> ListFiles(string agentId) =>
        FixedFileNames
            .Select(fileName => ReadFile(agentId, fileName))
            .ToList()
            .AsReadOnly();

    /// <summary>读取指定 DNA 文件；文件名非法时返回 null。</summary>
    public AgentDnaFileInfo? Read(string agentId, string fileName)
    {
        if (!IsAllowedFileName(fileName)) return null;
        return ReadFile(agentId, fileName);
    }

    /// <summary>更新指定 DNA 文件内容。文件名非法时返回 null。</summary>
    public AgentDnaFileInfo? Update(string agentId, string fileName, string content)
    {
        if (!IsAllowedFileName(fileName)) return null;

        string dir = AgentDir(agentId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(FilePath(agentId, fileName), content);
        return ReadFile(agentId, fileName);
    }

    // ── 构建 System Prompt ────────────────────────────────────────────────────

    /// <summary>
    /// 拼接 Agent SOUL + MEMORY（前 200 行）用于注入 System Prompt。
    /// 文件不存在或为空时跳过。
    /// </summary>
    public string BuildAgentContext(string agentId)
    {
        var parts = new List<string>(2);

        string soul = GetSoul(agentId).Trim();
        if (!string.IsNullOrWhiteSpace(soul))
            parts.Add(soul);

        string memory = GetMemory(agentId).Trim();
        if (!string.IsNullOrWhiteSpace(memory))
        {
            string[] lines = memory.Split('\n');
            string truncated = lines.Length > MemoryMaxLines
                ? string.Join('\n', lines.Take(MemoryMaxLines)) + $"\n\n<!-- Agent MEMORY 已截取前 {MemoryMaxLines} 行 -->"
                : memory;
            parts.Add($"## Agent 长期记忆\n\n{truncated}");
        }

        return string.Join("\n\n", parts);
    }

    // ── 清理 ─────────────────────────────────────────────────────────────────

    /// <summary>删除 Agent 的所有 DNA 文件和目录（Agent 删除时调用）。</summary>
    public void DeleteAgentFiles(string agentId)
    {
        string dir = AgentDir(agentId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────────────────

    private AgentDnaFileInfo ReadFile(string agentId, string fileName)
    {
        string path = FilePath(agentId, fileName);
        string content = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        DateTimeOffset updatedAt = File.Exists(path)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)
            : DateTimeOffset.MinValue;
        return new AgentDnaFileInfo(fileName, FileDescriptions[fileName], content, updatedAt);
    }
}
