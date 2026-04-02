using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MicroClaw.Configuration;

/// <summary>
/// 将 Options 对象序列化并写回对应的 YAML 配置文件。
/// 写入前自动创建 .bak 备份；若目标目录不存在则自动创建。
/// </summary>
public static class YamlSectionWriter
{
    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.Preserve)
        .Build();

    /// <summary>
    /// 将 <paramref name="value"/> 以 <c>{sectionKey}: ...</c> 格式写入 <paramref name="filePath"/>。
    /// </summary>
    public static void Write(string filePath, string sectionKey, object value)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        if (File.Exists(filePath))
            File.Copy(filePath, filePath + ".bak", overwrite: true);

        // Wrap in section key so the output is:  sectionKey:\n  field: value
        var wrapper = new Dictionary<string, object> { { sectionKey, value } };
        string yaml = Serializer.Serialize(wrapper);
        File.WriteAllText(filePath, yaml);
    }
}
