using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Reflection;

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
    /// 若文件被其他进程占用，最多重试 5 次（每次间隔 20ms）。
    /// </summary>
    public static void Write(string filePath, string sectionKey, object value)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // Wrap in section key so the output is:  sectionKey:\n  field: value
        var wrapper = new Dictionary<string, object> { { sectionKey, value } };
        string yaml = BuildDocument(value.GetType(), Serializer.Serialize(wrapper));

        const int maxRetries = 5;
        const int retryDelayMs = 20;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                if (File.Exists(filePath))
                    File.Copy(filePath, filePath + ".bak", overwrite: true);

                File.WriteAllText(filePath, yaml);
                return;
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                Thread.Sleep(retryDelayMs);
            }
        }
    }

    private static string BuildDocument(Type valueType, string body)
    {
        MicroClawYamlConfigAttribute? metadata = valueType.GetCustomAttribute<MicroClawYamlConfigAttribute>(inherit: false);
        if (string.IsNullOrWhiteSpace(metadata?.HeaderComment))
            return body;

        var commentLines = metadata.HeaderComment
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(static line => string.IsNullOrWhiteSpace(line) ? "#" : $"# {line}");

        return string.Join(Environment.NewLine, commentLines) + Environment.NewLine + Environment.NewLine + body;
    }
}
