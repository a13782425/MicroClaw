using System.Text.Json;

namespace MicroClaw.Abstractions.Streaming;

/// <summary>将 Dictionary&lt;string, object?&gt; 序列化为 Dictionary&lt;string, JsonElement&gt; 供 SessionMessage.Metadata 使用。</summary>
public static class MetadataHelper
{
    private static readonly JsonSerializerOptions Opts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IReadOnlyDictionary<string, JsonElement> ToJsonElements(Dictionary<string, object?> dict)
    {
        string json = JsonSerializer.Serialize(dict, Opts);
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, Opts) ?? new();
    }
}
