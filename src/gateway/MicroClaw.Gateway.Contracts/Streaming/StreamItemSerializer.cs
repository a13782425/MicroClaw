using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroClaw.Gateway.Contracts.Streaming;

/// <summary>
/// 将 <see cref="StreamItem"/> 子类序列化为 SSE data 行的 JSON 字符串。
/// 利用 <see cref="StreamItem.TypeName"/> 和 <see cref="StreamItem.ToSerializablePayload()"/>
/// 实现统一序列化，新增 StreamItem 子类型无需修改此处。
/// </summary>
public static class StreamItemSerializer
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // 保留非 ASCII 字符（中文等）不转义，使 SSE 输出更易读
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 将 <see cref="StreamItem"/> 序列化为 SSE JSON 字符串。
    /// 自动从子类型的 TypeName 和 ToSerializablePayload 构建 JSON。
    /// </summary>
    public static string Serialize(StreamItem item)
    {
        // 将 payload 序列化为 JsonDocument，然后合并 type 字段
        string payloadJson = JsonSerializer.Serialize(item.ToSerializablePayload(), Opts);
        using JsonDocument doc = JsonDocument.Parse(payloadJson);

        using var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Encoder = Opts.Encoder,
        }))
        {
            writer.WriteStartObject();
            writer.WriteString("type", item.TypeName);
            foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                prop.WriteTo(writer);
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
