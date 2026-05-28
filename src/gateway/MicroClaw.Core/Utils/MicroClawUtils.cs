using System.Text.Json;
using System.Text.RegularExpressions;
namespace MicroClaw.Utils;
/// <summary>
/// MicroClaw 工具类集合。
/// </summary>
public static class MicroClawUtils
{
    private static readonly Regex EnvVarPattern = new(@"\$\{([^}]+)\}", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    /// <summary>
    /// 获取一个唯一字符串，通常用于标识 Session、Agent 实体等需要唯一标识的场景。
    /// </summary>
    /// <returns></returns>
    public static string GetUniqueId() => Guid.NewGuid().ToString("N");

    public static void CheckDirectory(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }

    [Obsolete]
    public static List<T> DeserializeList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<T[]>(json, JsonOpts)?.ToList() ?? [];
    }

    /// <summary>
    /// 解析字符串中的环境变量占位符，格式为 `${VAR_NAME}`，并替换为对应的环境变量值。
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public static string? ResolveEnv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return EnvVarPattern.Replace(value, m => Environment.GetEnvironmentVariable(m.Groups[1].Value) ?? m.Value);
    }
}