using System.Text.Json;
namespace MicroClaw.Utils;
/// <summary>
/// MicroClaw 工具类集合。
/// </summary>
public static class MicroClawUtils
{
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
    
    public static List<T> DeserializeList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<T[]>(json, JsonOpts)?.ToList() ?? [];
    }
    
}