using System.Reflection;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization.NamingConventions;

namespace MicroClaw.Configuration;

public sealed class YamlConfigStore
{
    private static readonly ISerializer Serializer = new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).ConfigureDefaultValuesHandling(DefaultValuesHandling.Preserve).Build();
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).IgnoreUnmatchedProperties().Build();
    private readonly string _configRootDir;
    
    public YamlConfigStore(string configRootDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configRootDir);
        
        _configRootDir = configRootDir;
    }
    public T? Get<T>(string? directoryPath = null, string? fileName = null) where T : class, new()
    {
        YamlDocumentDescriptor descriptor = CreateDescriptor<T>(directoryPath, fileName, requireFileName: false);
        
        if (string.IsNullOrWhiteSpace(descriptor.FileName))
            return null;
        
        string filePath = ResolveFilePath(descriptor);
        if (!File.Exists(filePath))
            return null;
        
        string content = File.ReadAllText(filePath);
        if (string.IsNullOrWhiteSpace(content))
            return null;
        
        string? sectionYaml = ExtractSectionYaml(content, descriptor.SectionKey);
        if (string.IsNullOrWhiteSpace(sectionYaml))
            return null;
        
        return Deserializer.Deserialize<T>(sectionYaml);
    }
    
    public T Update<T>(Func<T, T> updater, string? directoryPath = null, string? fileName = null) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(updater);
        
        T current = Get<T>(directoryPath, fileName) ?? new T();
        T next = updater(current) ?? throw new InvalidOperationException("配置更新委托不能返回 null。");
        
        return Save(next, directoryPath, fileName);
    }
    
    public T Save<T>(T value, string? directoryPath = null, string? fileName = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        
        YamlDocumentDescriptor descriptor = CreateDescriptor<T>(directoryPath, fileName, requireFileName: true);
        string filePath = ResolveFilePath(descriptor);
        string yaml = BuildDocument(descriptor, value);
        
        WriteAllTextWithBackup(filePath, yaml);
        return value;
    }
    
    public bool Delete<T>(string? directoryPath = null, string? fileName = null) where T : class
    {
        YamlDocumentDescriptor descriptor = CreateDescriptor<T>(directoryPath, fileName, requireFileName: false);
        
        if (string.IsNullOrWhiteSpace(descriptor.FileName))
            return false;
        
        string filePath = ResolveFilePath(descriptor);
        if (!File.Exists(filePath))
            return false;
        
        const int maxRetries = 5;
        const int retryDelayMs = 20;
        
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                File.Copy(filePath, filePath + ".bak", overwrite: true);
                File.Delete(filePath);
                return true;
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                Thread.Sleep(retryDelayMs);
            }
        }
        
        return false;
    }
    
    private YamlDocumentDescriptor CreateDescriptor<T>(string? directoryPath, string? fileName, bool requireFileName) where T : class
    {
        Type valueType = typeof(T);
        MicroClawYamlConfigAttribute? metadata = valueType.GetCustomAttribute<MicroClawYamlConfigAttribute>(inherit: false);
        
        if (metadata is null)
            throw new InvalidOperationException($"配置类型 {valueType.Name} 缺少 [MicroClawYamlConfig]。");
        
        string sectionKey = metadata.SectionKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(sectionKey))
            throw new InvalidOperationException($"配置类型 {valueType.Name} 的 SectionKey 不能为空。");
        
        string? resolvedFileName = string.IsNullOrWhiteSpace(fileName) ? NormalizeNullable(metadata.FileName) : fileName.Trim();
        
        if (requireFileName && string.IsNullOrWhiteSpace(resolvedFileName))
            throw new InvalidOperationException($"配置类型 {valueType.Name} 缺少可落盘的 FileName 元数据。");
        
        if (!string.IsNullOrWhiteSpace(resolvedFileName))
            MicroClawConfigPathResolver.EnsureSafeFileName(valueType, resolvedFileName);
        
        string? resolvedDirectoryPath = ResolveDirectoryPath(valueType, directoryPath);
        
        return new YamlDocumentDescriptor(valueType, sectionKey, resolvedFileName, resolvedDirectoryPath, NormalizeNullable(metadata.HeaderComment));
    }

    private string? ResolveDirectoryPath(Type valueType, string? explicitDirectoryPath)
    {
        string? candidate = NormalizeNullable(explicitDirectoryPath);
        
        if (string.IsNullOrWhiteSpace(candidate))
            return null;
        
        return MicroClawConfigPathResolver.NormalizeDirectoryPath(valueType, candidate, _configRootDir);
    }

    private string ResolveFilePath(YamlDocumentDescriptor descriptor)
    {
        return MicroClawConfigPathResolver.ResolveFilePath(_configRootDir, descriptor.ValueType, descriptor.FileName, descriptor.DirectoryPath);
    }
    
    private static string BuildDocument(YamlDocumentDescriptor descriptor, object value)
    {
        var wrapper = new Dictionary<string, object> { [descriptor.SectionKey] = value };
        
        string body = Serializer.Serialize(wrapper);
        
        if (string.IsNullOrWhiteSpace(descriptor.HeaderComment))
            return body;
        
        IEnumerable<string> commentLines = descriptor.HeaderComment.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(static line => string.IsNullOrWhiteSpace(line) ? "#" : $"# {line}");
        
        return string.Join(Environment.NewLine, commentLines) + Environment.NewLine + Environment.NewLine + body;
    }

    private static string? ExtractSectionYaml(string content, string sectionKey)
    {
        using var reader = new StringReader(content);
        var yaml = new YamlStream();
        yaml.Load(reader);
        
        if (yaml.Documents.Count == 0)
            return null;
        
        if (yaml.Documents[0].RootNode is not YamlMappingNode root)
            return null;
        
        foreach ((YamlNode keyNode, YamlNode valueNode) in root.Children)
        {
            if (keyNode is not YamlScalarNode key)
                continue;
            
            if (!string.Equals(key.Value, sectionKey, StringComparison.OrdinalIgnoreCase))
                continue;
            
            return SerializeNode(valueNode);
        }
        
        return null;
    }

    private static string SerializeNode(YamlNode node)
    {
        var stream = new YamlStream(new YamlDocument(node));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }
    
    private static void WriteAllTextWithBackup(string filePath, string yaml)
    {
        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        
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

    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record YamlDocumentDescriptor(Type ValueType, string SectionKey, string? FileName, string? DirectoryPath, string? HeaderComment);
}