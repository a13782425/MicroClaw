namespace MicroClaw.Configuration;
/// <summary>
/// 解析 .env 文件，将键值对写入进程环境变量（已存在的变量不覆盖）。
/// </summary>
public static class DotEnvLoader
{
    /// <summary>
    /// 加载指定路径的 .env 文件。文件不存在时静默返回。
    /// </summary>
    public static void Load()
    {
        var home = Environment.GetEnvironmentVariable(MICROCLAW_HOME);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = HomeInitializer.ResolveHome(home: null);
            Environment.SetEnvironmentVariable(MICROCLAW_HOME, home);
        }

        HomeInitializer.EnsureLegacyConfigContractIsAbsent(home);

        string envPath = Path.Combine(home, ".env");
        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith('#') || !trimmed.Contains('=')) continue;
                
                var idx = trimmed.IndexOf('=');
                var key = trimmed[..idx].Trim();
                if (key == MICROCLAW_HOME)
                    throw new InvalidOperationException("MICROCLAW_HOME 不能定义在 .env 中。请在启动进程前通过外部环境变量设置它。");
                if (key == "MICROCLAW_CONFIG_FILE")
                    throw new InvalidOperationException("MICROCLAW_CONFIG_FILE 已废弃。请改用 MICROCLAW_HOME 指向工作目录，并将配置迁移到 HOME/config/*.yaml。");
                var value = trimmed[(idx + 1)..].Trim().Trim('"').Trim('\'');
                
                if (!string.IsNullOrEmpty(key) && Environment.GetEnvironmentVariable(key) is null)
                    Environment.SetEnvironmentVariable(key, value);
            }
        }
        
        InitDefaultEnv();
        // 确保工作目录和默认文件存在（不覆盖用户已有文件）
        HomeInitializer.EnsureInitialized(Environment.GetEnvironmentVariable(MICROCLAW_HOME));
    }

    private static void InitDefaultEnv()
    {
        var home = Environment.GetEnvironmentVariable(MICROCLAW_HOME);
        //写入默认值
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GATEWAY_HOST)))
            Environment.SetEnvironmentVariable(GATEWAY_HOST, "localhost");
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(GATEWAY_PORT)))
            Environment.SetEnvironmentVariable(GATEWAY_PORT, "5080");
        
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(MICROCLAW_WEBUI_PATH)))
            Environment.SetEnvironmentVariable(MICROCLAW_WEBUI_PATH, Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"));
        
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
        {
            var gatewayHost = Environment.GetEnvironmentVariable(GATEWAY_HOST);
            var gatewayPort = Environment.GetEnvironmentVariable(GATEWAY_PORT);
            Environment.SetEnvironmentVariable("ASPNETCORE_URLS", $"http://{gatewayHost}:{gatewayPort}");
        }
    }
}