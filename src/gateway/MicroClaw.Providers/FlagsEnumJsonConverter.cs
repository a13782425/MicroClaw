using System.Text.Json;
using System.Text.Json.Serialization;

namespace MicroClaw.Providers;

/// <summary>
/// 将 [Flags] 枚举序列化为字符串数组（如 InputModality.Text|Image → ["Text","Image"]），
/// 反序列化时按位 OR 还原。容错：未识别的成员名静默跳过，None 不写入数组。
/// </summary>
public abstract class FlagsEnumStringArrayConverter<T> : JsonConverter<T> where T : struct, Enum
{
    private static readonly T[] _flags = BuildFlagList();

    private static T[] BuildFlagList()
    {
        var values = (T[])Enum.GetValues(typeof(T));
        var result = new List<T>(values.Length);
        foreach (T v in values)
        {
            ulong bits = Convert.ToUInt64(v);
            if (bits == 0) continue;
            // 仅保留 2 的幂（单一 bit），避免组合常量重复输出
            if ((bits & (bits - 1)) != 0) continue;
            result.Add(v);
        }
        return result.ToArray();
    }

    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // 兼容 null
        if (reader.TokenType == JsonTokenType.Null)
            return default;

        // 兼容数字直接 cast
        if (reader.TokenType == JsonTokenType.Number)
        {
            ulong num = reader.GetUInt64();
            return (T)Enum.ToObject(typeof(T), num);
        }

        // 兼容单个字符串
        if (reader.TokenType == JsonTokenType.String)
        {
            string? s = reader.GetString();
            return ParseSingle(s);
        }

        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException($"Expected array, string or number for flags enum {typeof(T).Name}, got {reader.TokenType}");

        ulong acc = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray) break;
            if (reader.TokenType != JsonTokenType.String) continue;
            string? name = reader.GetString();
            if (string.IsNullOrEmpty(name)) continue;
            if (Enum.TryParse(typeof(T), name, ignoreCase: true, out object? parsed))
                acc |= Convert.ToUInt64((T)parsed!);
        }
        return (T)Enum.ToObject(typeof(T), acc);
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        ulong bits = Convert.ToUInt64(value);
        foreach (T flag in _flags)
        {
            ulong fbits = Convert.ToUInt64(flag);
            if ((bits & fbits) == fbits)
                writer.WriteStringValue(flag.ToString());
        }
        writer.WriteEndArray();
    }

    private static T ParseSingle(string? name)
    {
        if (string.IsNullOrEmpty(name)) return default;
        return Enum.TryParse(typeof(T), name, ignoreCase: true, out object? parsed)
            ? (T)parsed!
            : default;
    }
}

public sealed class InputModalityJsonConverter : FlagsEnumStringArrayConverter<InputModality> { }
public sealed class OutputModalityJsonConverter : FlagsEnumStringArrayConverter<OutputModality> { }
public sealed class ProviderFeatureJsonConverter : FlagsEnumStringArrayConverter<ProviderFeature> { }
