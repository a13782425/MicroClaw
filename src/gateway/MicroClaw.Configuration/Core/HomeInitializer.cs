namespace MicroClaw.Configuration;

/// <summary>
/// 共享初始化逻辑：解析工作目录、创建子目录、写入默认配置文件。
/// </summary>
public static class HomeInitializer
{
    private static readonly (string RelativePath, string Content)[] ConfigFiles =
    [
        ("microclaw.yaml",         InitDefaults.MicroclawYaml),
        ("config/auth.yaml",       InitDefaults.AuthYaml),
        ("config/channels.yaml",   InitDefaults.ChannelsYaml),
        ("config/logging.yaml",    InitDefaults.LoggingYaml),
        ("config/skills.yaml",     InitDefaults.SkillsYaml),
        ("config/emotion.yaml",    InitDefaults.EmotionYaml),
        ("config/agents.yaml",     InitDefaults.AgentsYaml),
        ("config/sessions.yaml",   InitDefaults.SessionsYaml),
        ("config/providers.yaml",  InitDefaults.ProvidersYaml),
        (".env",           InitDefaults.DotEnvExample),
    ];

    private static readonly string[] SubDirectories =
    [
        "config",
        "logs",
        "workspace/sessions",
        "workspace/skills",
        "workspace/cron",
    ];

    /// <summary>
    /// 解析工作目录路径，优先级：MICROCLAW_HOME > configFile 同级目录 > ./.microclaw
    /// </summary>
    public static string ResolveHome(string? home, string? configFile)
    {
        if (!string.IsNullOrWhiteSpace(home))
            return home;

        if (!string.IsNullOrWhiteSpace(configFile))
            return Path.GetDirectoryName(Path.GetFullPath(configFile))!;

        return Path.Combine(Directory.GetCurrentDirectory(), ".microclaw");
    }

    /// <summary>
    /// 确保工作目录存在并包含所有必要的子目录和默认配置文件。
    /// </summary>
    /// <param name="home">MICROCLAW_HOME 环境变量值（可为 null）</param>
    /// <param name="configFile">MICROCLAW_CONFIG_FILE 环境变量值（可为 null）</param>
    /// <param name="force">true 时覆盖已存在的配置文件</param>
    /// <param name="verbose">true 时向控制台输出 created/skipped 信息</param>
    public static void EnsureInitialized(
        string? home,
        string? configFile,
        bool force = false,
        bool verbose = false)
    {
        string homeDir = ResolveHome(home, configFile);

        // 创建所有子目录
        foreach (string subDir in SubDirectories)
        {
            string fullPath = Path.Combine(homeDir, subDir);
            Directory.CreateDirectory(fullPath);
        }

        // 写入默认配置文件
        foreach ((string relativePath, string content) in ConfigFiles)
        {
            string fullPath = Path.Combine(homeDir, relativePath);
            string? dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            if (File.Exists(fullPath) && !force)
            {
                if (verbose)
                    Console.WriteLine($"  skipped  {relativePath}");
                continue;
            }

            File.WriteAllText(fullPath, content);
            if (verbose)
                Console.WriteLine($"  created  {relativePath}");
        }
    }
}
