namespace MicroClaw.Configuration;

/// <summary>
/// 解析 .env 文件，将键值对写入进程环境变量（已存在的变量不覆盖）。
/// </summary>
public static class DotEnvLoader
{
    /// <summary>
    /// 加载指定路径的 .env 文件。文件不存在时静默返回。
    /// </summary>
    public static void Load(string path)
    {
        if (!File.Exists(path)) return;

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith('#') || !trimmed.Contains('=')) continue;

            var idx = trimmed.IndexOf('=');
            var key = trimmed[..idx].Trim();
            var value = trimmed[(idx + 1)..].Trim().Trim('"').Trim('\'');

            if (!string.IsNullOrEmpty(key) && Environment.GetEnvironmentVariable(key) is null)
                Environment.SetEnvironmentVariable(key, value);
        }
    }
}
