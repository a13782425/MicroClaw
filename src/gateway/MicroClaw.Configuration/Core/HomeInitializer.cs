namespace MicroClaw.Configuration;

/// <summary>
/// 共享初始化逻辑：解析工作目录、创建子目录、写入默认配置文件。
/// </summary>
public static class HomeInitializer
{
    private static readonly (string RelativePath, string Content)[] ConfigFiles =
    [
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
    /// 解析工作目录路径，优先级：MICROCLAW_HOME > ./.microclaw
    /// </summary>
    public static string ResolveHome(string? home)
    {
        if (!string.IsNullOrWhiteSpace(home))
            return Path.GetFullPath(home);

        return Path.Combine(Directory.GetCurrentDirectory(), ".microclaw");
    }

    /// <summary>
    /// 无兼容模式下，旧主配置契约必须显式失败，避免静默回退到错误工作目录。
    /// </summary>
    public static void EnsureLegacyConfigContractIsAbsent(string? home)
    {
        string? legacyConfigFile = Environment.GetEnvironmentVariable("MICROCLAW_CONFIG_FILE");
        if (!string.IsNullOrWhiteSpace(legacyConfigFile))
        {
            throw new InvalidOperationException(
                "MICROCLAW_CONFIG_FILE 已废弃。请改用 MICROCLAW_HOME 指向工作目录，并将配置迁移到 HOME/config/*.yaml。");
        }

        string homeDir = ResolveHome(home);
        string legacyMainConfigPath = Path.Combine(homeDir, "microclaw.yaml");
        if (File.Exists(legacyMainConfigPath))
        {
            throw new InvalidOperationException(
                "microclaw.yaml 主配置已废弃。请将配置迁移到 HOME/config/*.yaml 后再启动。");
        }
    }

    /// <summary>
    /// 确保工作目录存在并包含所有必要的子目录和默认配置文件。
    /// </summary>
    /// <param name="home">MICROCLAW_HOME 环境变量值（可为 null）</param>
    /// <param name="force">true 时覆盖已存在的配置文件</param>
    /// <param name="verbose">true 时向控制台输出 created/skipped 信息</param>
    public static void EnsureInitialized(
        string? home,
        bool force = false,
        bool verbose = false)
    {
        string homeDir = ResolveHome(home);

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
