using System.Text.RegularExpressions;

namespace MicroClaw.Tools;

/// <summary>
/// 提取 MCP Server 配置字段中的 <c>${VAR}</c> 环境变量占位符，并检测其设置状态（用于 UI 展示）。
/// 注意：运行时传输层的占位符展开由 <see cref="McpConfigurationResolver"/> 负责。
/// </summary>
public static partial class EnvVarResolver
{
    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled)]
    private static partial Regex PlaceholderRegex();

    /// <summary>
    /// 扫描配置中所有字符串字段，提取 <c>${VAR}</c> 占位符列表及其 resolved 状态。
    /// 每个变量名只出现一次（以第一次出现的 foundIn 为准）。
    /// </summary>
    public static IReadOnlyList<McpEnvVarInfo> ExtractPlaceholders(McpServerConfig config)
    {
        var result = new Dictionary<string, McpEnvVarInfo>(StringComparer.Ordinal);

        void Add(string? value, string foundIn)
        {
            if (value is null) return;
            foreach (Match m in PlaceholderRegex().Matches(value))
            {
                string varName = m.Groups["name"].Value;
                if (result.ContainsKey(varName)) continue;
                bool isSet = Environment.GetEnvironmentVariable(varName) is not null;
                result[varName] = new McpEnvVarInfo(varName, isSet, foundIn);
            }
        }

        Add(config.Command, "command");
        if (config.Args is not null)
            foreach (string arg in config.Args) Add(arg, "args");
        if (config.Env is not null)
            foreach (KeyValuePair<string, string?> kv in config.Env) Add(kv.Value, "env");
        Add(config.Url, "url");
        if (config.Headers is not null)
            foreach (KeyValuePair<string, string> kv in config.Headers) Add(kv.Value, "headers");

        return result.Values.ToList().AsReadOnly();
    }
}

/// <summary>MCP Server 配置中检测到的环境变量占位符信息。</summary>
/// <param name="Name">环境变量名（如 <c>GITHUB_PERSONAL_ACCESS_TOKEN</c>）。</param>
/// <param name="IsSet">该变量是否已在当前进程环境中设置。</param>
/// <param name="FoundIn">占位符所在字段（command / args / env / url / headers）。</param>
public sealed record McpEnvVarInfo(string Name, bool IsSet, string FoundIn);
