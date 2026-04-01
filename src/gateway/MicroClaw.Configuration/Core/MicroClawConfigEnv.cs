namespace MicroClaw.Configuration;

/// <summary>
/// 环境变量和路径解析。通过 <see cref="MicroClawConfig.Env"/> 访问。
/// 所有路径属性在 <see cref="Initialize"/> 时一次性解析并缓存。
/// </summary>
public sealed class MicroClawConfigEnv
{
    /// <summary>MICROCLAW_HOME 工作目录。</summary>
    public string Home { get; }

    /// <summary>主配置文件路径（microclaw.yaml）。</summary>
    public string? ConfigFile { get; }

    /// <summary>SQLite 数据库文件完整路径。</summary>
    public string DbPath { get; }

    /// <summary>会话消息历史存储目录（workspace/sessions/）。</summary>
    public string SessionsDir { get; }

    /// <summary>workspace 根目录。</summary>
    public string WorkspaceRoot { get; }

    /// <summary>Agent DNA 文件目录（workspace/agents/）。</summary>
    public string AgentsDir { get; }

    /// <summary>插件目录（workspace/plugins/）。</summary>
    public string PluginsDir { get; }

    /// <summary>滚动日志文件路径（含日期占位符）。</summary>
    public string LogFilePath { get; }
    
    internal MicroClawConfigEnv()
    {
        string? home = Get(MICROCLAW_HOME);
        string? configFile = Get(MICROCLAW_CONFIG_FILE);
        
        Home = HomeInitializer.ResolveHome(home, configFile);
        ConfigFile = configFile;

        DbPath = ResolveDatabasePath(home, configFile);
        SessionsDir = ResolveSessionsDir(home, configFile);
        WorkspaceRoot = ResolveWorkspaceRoot(home, configFile);
        AgentsDir = Path.Combine(WorkspaceRoot, "agents");
        PluginsDir = Path.Combine(WorkspaceRoot, "plugins");
        LogFilePath = ResolveLogFilePath(home, configFile);
    }

    /// <summary>
    /// 获取环境变量原始值。
    /// </summary>
    public string? Get(string key) => Environment.GetEnvironmentVariable(key);

    // ── 路径解析逻辑（从 ServeCommand 迁入） ─────────────────────────────────

    private static string ResolveDatabasePath(string? home, string? configFile)
    {
        string dir;
        if (!string.IsNullOrWhiteSpace(home))
            dir = home;
        else if (!string.IsNullOrWhiteSpace(configFile))
            dir = Path.GetDirectoryName(Path.GetFullPath(configFile))!;
        else
            dir = Path.Combine(Directory.GetCurrentDirectory(), ".microclaw");

        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "microclaw.db");
    }

    private static string ResolveSessionsDir(string? home, string? configFile)
    {
        if (!string.IsNullOrWhiteSpace(home))
            return Path.Combine(home, "workspace", "sessions");
        if (!string.IsNullOrWhiteSpace(configFile))
            return Path.Combine(Path.GetDirectoryName(configFile)!, "workspace", "sessions");
        return Path.Combine(Directory.GetCurrentDirectory(), ".microclaw", "workspace", "sessions");
    }

    private static string ResolveWorkspaceRoot(string? home, string? configFile)
    {
        if (!string.IsNullOrWhiteSpace(home))
            return Path.Combine(home, "workspace");
        if (!string.IsNullOrWhiteSpace(configFile))
            return Path.Combine(Path.GetDirectoryName(configFile)!, "workspace");
        return Path.Combine(Directory.GetCurrentDirectory(), ".microclaw", "workspace");
    }

    private static string ResolveLogFilePath(string? home, string? configFile)
    {
        string logsDir;
        if (!string.IsNullOrWhiteSpace(home))
            logsDir = Path.Combine(home, "logs");
        else if (!string.IsNullOrWhiteSpace(configFile))
            logsDir = Path.Combine(Path.GetDirectoryName(configFile)!, "logs");
        else
            logsDir = Path.Combine(Directory.GetCurrentDirectory(), ".microclaw", "logs");

        Directory.CreateDirectory(logsDir);
        return Path.Combine(logsDir, "microclaw-.log");
    }
    
}
