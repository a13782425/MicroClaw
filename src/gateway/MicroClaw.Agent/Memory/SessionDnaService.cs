namespace MicroClaw.Agent.Memory;

/// <summary>Session DNA 固定文件信息。</summary>
public sealed record SessionDnaFileInfo(
    string FileName,
    string Description,
    string Content,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Session DNA 固定文件服务：每个 Session 拥有两个固定 DNA 文件（USER / AGENTS），
/// 不可新建或删除，只能编辑内容。文件直接存储于 {sessionsDir}/{sessionId}/ 下。
/// SOUL.md 已移至 Agent 级别管理（AgentDnaService），旧 Session 中已有的 SOUL.md 仍可读写以保持向后兼容。
/// </summary>
public sealed class SessionDnaService(string sessionsDir)
{
    // ── 常量 ─────────────────────────────────────────────────────────────────

    /// <summary>当前活跃的固定文件（新 Session 初始化只生成这两个）。</summary>
    public static readonly IReadOnlyList<string> FixedFileNames =
        ["USER.md", "AGENTS.md"];

    /// <summary>所有允许读写的文件名（含已废弃的 SOUL.md 以保持向后兼容）。</summary>
    private static readonly IReadOnlyDictionary<string, string> FileDescriptions =
        new Dictionary<string, string>
        {
            ["SOUL.md"] = "定义 AI 的人格、语气和表达风格（已移至 Agent 级别）",
            ["USER.md"] = "定义对话对象的画像、偏好和背景信息",
            ["AGENTS.md"] = "定义工作流、决策规则和处理步骤",
        };

    private static readonly IReadOnlyDictionary<string, string> DefaultTemplates =
        new Dictionary<string, string>
        {
            ["USER.md"] = """
                # User — 用户画像

                在此描述对话对象的信息、偏好和背景。

                例如：
                - 职业背景：技术总监，10 年行业经验
                - 偏好：喜欢具体示例和数据支撑
                - 语言：中文优先，接受技术英文术语
                """,

            ["AGENTS.md"] = """
                # Agents — 工作流与处理规则

                在此定义 AI 的处理流程、决策规则和工作步骤。

                例如：
                - 收到需求时：先确认核心目标，再给出方案
                - 不确定时：列出选项并说明各自权衡
                - 处理技术问题：优先最小可行方案
                """,
        };

    // ── 路径辅助 ──────────────────────────────────────────────────────────────

    private string SessionDir(string sessionId) => Path.Combine(sessionsDir, sessionId);

    private string FilePath(string sessionId, string fileName) =>
        Path.Combine(SessionDir(sessionId), fileName);

    // ── 校验 ─────────────────────────────────────────────────────────────────

    /// <summary>检查文件名是否属于允许的固定文件（含已废弃的 SOUL.md）。</summary>
    public static bool IsAllowedFileName(string fileName) =>
        FileDescriptions.ContainsKey(fileName);

    // ── 初始化 ────────────────────────────────────────────────────────────────

    /// <summary>
    /// 新建 Session 时自动初始化 DNA 文件（幂等，已存在则跳过）。
    /// 只初始化 USER.md 和 AGENTS.md（SOUL.md 已移至 Agent 级别）。
    /// </summary>
    public void InitializeSession(string sessionId)
    {
        string dir = SessionDir(sessionId);
        Directory.CreateDirectory(dir);

        foreach (string fileName in FixedFileNames)
        {
            string path = FilePath(sessionId, fileName);
            if (!File.Exists(path))
                File.WriteAllText(path, DefaultTemplates[fileName]);
        }
    }

    // ── 读取 ─────────────────────────────────────────────────────────────────

    /// <summary>列出固定 DNA 文件（包含内容和元数据）。</summary>
    public IReadOnlyList<SessionDnaFileInfo> ListFiles(string sessionId) =>
        FixedFileNames
            .Select(fileName => ReadFile(sessionId, fileName))
            .ToList()
            .AsReadOnly();

    /// <summary>读取指定 DNA 文件；文件名非法时返回 null。</summary>
    public SessionDnaFileInfo? Read(string sessionId, string fileName)
    {
        if (!IsAllowedFileName(fileName)) return null;
        return ReadFile(sessionId, fileName);
    }

    // ── 更新 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 更新指定 DNA 文件内容。文件名非法时返回 null；文件目录自动创建。
    /// </summary>
    public SessionDnaFileInfo? Update(string sessionId, string fileName, string content)
    {
        if (!IsAllowedFileName(fileName)) return null;

        string dir = SessionDir(sessionId);
        Directory.CreateDirectory(dir);

        string path = FilePath(sessionId, fileName);
        File.WriteAllText(path, content);

        return ReadFile(sessionId, fileName);
    }

    // ── 构建 System Prompt ────────────────────────────────────────────────────

    /// <summary>
    /// 将 DNA 文件内容按顺序拼接，用于注入 System Prompt。
    /// 只拼接 USER.md + AGENTS.md（SOUL.md 已移至 Agent 级别）。
    /// 文件不存在或为空时跳过。
    /// </summary>
    public string BuildDnaContext(string sessionId)
    {
        var parts = new List<string>();

        foreach (string fileName in FixedFileNames)
        {
            string path = FilePath(sessionId, fileName);
            if (!File.Exists(path)) continue;

            string content = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(content)) continue;

            parts.Add(content);
        }

        return string.Join("\n\n", parts);
    }

    // ── 清理 ─────────────────────────────────────────────────────────────────

    /// <summary>删除 Session 的固定 DNA 文件（Session 删除时调用）。</summary>
    public void DeleteSessionDnaFiles(string sessionId)
    {
        // 同时清理旧 SOUL.md（如果存在）
        string[] filesToDelete = ["SOUL.md", .. FixedFileNames];
        foreach (string fileName in filesToDelete)
        {
            string path = FilePath(sessionId, fileName);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    // ── 私有辅助 ──────────────────────────────────────────────────────────────

    private SessionDnaFileInfo ReadFile(string sessionId, string fileName)
    {
        string path = FilePath(sessionId, fileName);
        string content = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        DateTimeOffset updatedAt = File.Exists(path)
            ? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero)
            : DateTimeOffset.MinValue;
        return new SessionDnaFileInfo(fileName, FileDescriptions[fileName], content, updatedAt);
    }
}
