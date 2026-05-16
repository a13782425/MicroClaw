using System.Collections.Concurrent;
using System.Reflection;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization.NamingConventions;

namespace MicroClaw.Configuration;
public sealed class YamlConfigStore
{
    private static readonly ISerializer Serializer = new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).ConfigureDefaultValuesHandling(DefaultValuesHandling.Preserve).Build();
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).IgnoreUnmatchedProperties().Build();
    private readonly string _configRootDir;
    private readonly ConcurrentDictionary<string, object> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, MicroClawConfigTypeDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _descriptorCacheLock = new();
    public YamlConfigStore(string configRootDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configRootDir);
        _configRootDir = configRootDir;
    }
    public T? Get<T>(string? fileName = null, string? directoryPath = null) where T : class, new()
    {
        MicroClawConfigTypeDescriptor descriptor = CreateDescriptor<T>(directoryPath, fileName);
        
        if (string.IsNullOrWhiteSpace(descriptor.FileName))
            return null;
        
        string filePath = ResolveFilePath(descriptor);
        
        if (_cache.TryGetValue(filePath, out object? cached))
            return (T)cached;
        
        T? result = null;
        
        if (File.Exists(filePath))
        {
            string content = File.ReadAllText(filePath);
            if (!string.IsNullOrWhiteSpace(content))
            {
                string? sectionYaml = ExtractSectionYaml(content, descriptor.SectionKey);
                if (!string.IsNullOrWhiteSpace(sectionYaml))
                    result = Deserializer.Deserialize<T>(sectionYaml);
            }
        }
        
        if (result is not null)
        {
            _cache[filePath] = result;
            return result;
        }
        
        // No file or section found — apply fallback
        T placeholder = new T();
        if (placeholder is IMicroClawConfigTemplate templateProvider)
        {
            IMicroClawConfigOptions template = templateProvider.CreateDefaultTemplate()
                ?? throw new InvalidOperationException($"配置类型 {typeof(T).Name} 的默认模板不能为空。");
            if (template is not T typedTemplate)
                throw new InvalidOperationException($"配置类型 {typeof(T).Name} 的默认模板实例类型必须与 {typeof(T).Name} 兼容。");
            Save(typedTemplate, fileName, directoryPath);
            return typedTemplate;
        }
        
        return placeholder;
    }
    
    public T Save<T>(T value, string? fileName = null, string? directoryPath = null) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        
        MicroClawConfigTypeDescriptor descriptor = CreateDescriptor<T>(directoryPath, fileName);
        string filePath = ResolveFilePath(descriptor);
        string yaml = BuildDocument(descriptor, value);
        
        WriteAllTextWithBackup(filePath, yaml);
        _cache[filePath] = value;
        return value;
    }
    
    public bool Delete<T>(string? fileName = null, string? directoryPath = null) where T : class
    {
        MicroClawConfigTypeDescriptor descriptor = CreateDescriptor<T>(directoryPath, fileName);
        
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
                _cache.TryRemove(filePath, out _);
                return true;
            }
            catch (IOException) when (attempt < maxRetries - 1)
            {
                Thread.Sleep(retryDelayMs);
            }
        }
        
        return false;
    }
    
    private MicroClawConfigTypeDescriptor CreateDescriptor<T>(string? directoryPath, string? fileName) where T : class
    {
        Type valueType = typeof(T);
        bool implementsContract = typeof(IMicroClawConfigOptions).IsAssignableFrom(valueType);
        MicroClawYamlConfigAttribute? metadata = valueType.GetCustomAttribute<MicroClawYamlConfigAttribute>(inherit: false);
        
        if (!implementsContract && metadata is null)
            throw new InvalidOperationException($"配置类型 {valueType.Name} 必须同时实现 {nameof(IMicroClawConfigOptions)} 并标注 [MicroClawYamlConfig]。");
        
        if (!implementsContract)
            throw new InvalidOperationException($"配置类型 {valueType.Name} 标注了 [MicroClawYamlConfig]，但未实现 {nameof(IMicroClawConfigOptions)}。");
        
        if (metadata is null)
            throw new InvalidOperationException($"配置类型 {valueType.Name} 缺少 [MicroClawYamlConfig]。");
        
        if (!valueType.IsClass || valueType.IsAbstract)
            throw new InvalidOperationException($"配置类型 {valueType.Name} 必须是可实例化的具体 class。");
        
        if (valueType.GetConstructor(Type.EmptyTypes) is null)
            throw new InvalidOperationException($"配置类型 {valueType.Name} 必须提供无参构造函数以支持配置绑定。");
        
        string sectionKey = metadata.SectionKey?.Trim() ?? valueType.Name.ToLower();
        if (string.IsNullOrWhiteSpace(sectionKey))
            throw new InvalidOperationException($"配置类型 {valueType.Name} 的 SectionKey 不能为空。");
        
        string resolvedFileName;
        if (!string.IsNullOrWhiteSpace(fileName))
            resolvedFileName = fileName.Trim();
        else if (NormalizeNullable(metadata.FileName) is { } normalizedFileName)
            resolvedFileName = normalizedFileName;
        else
            resolvedFileName = valueType.Name.ToLower() + ".yaml";
        
        MicroClawConfigPathResolver.EnsureSafeFileName(valueType, resolvedFileName);
        
        string? resolvedDirectoryPath = ResolveDirectoryPath(valueType, directoryPath);
        string cacheKey = MicroClawConfigPathResolver.ResolveFilePath(_configRootDir, valueType, resolvedFileName, resolvedDirectoryPath);
        
        lock (_descriptorCacheLock)
        {
            if (_descriptors.TryGetValue(cacheKey, out MicroClawConfigTypeDescriptor? cachedDescriptor))
                return cachedDescriptor;
            MicroClawConfigTypeDescriptor descriptor = new MicroClawConfigTypeDescriptor(valueType, sectionKey, resolvedFileName, resolvedDirectoryPath, NormalizeNullable(metadata.HeaderComment));
            if (_descriptors.TryGetValue(cacheKey, out MicroClawConfigTypeDescriptor? racedDescriptor))
                return racedDescriptor;
            _descriptors[cacheKey] = descriptor;
            return descriptor;
        }
    }
    
    private string? ResolveDirectoryPath(Type valueType, string? explicitDirectoryPath)
    {
        string? candidate = NormalizeNullable(explicitDirectoryPath);
        
        if (string.IsNullOrWhiteSpace(candidate))
            return null;
        
        return MicroClawConfigPathResolver.NormalizeDirectoryPath(valueType, candidate, _configRootDir);
    }
    
    private string ResolveFilePath(MicroClawConfigTypeDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.FileName))
            throw new InvalidOperationException($"配置类型 {descriptor.YamlConfigType.Name} 缺少可落盘的 FileName 元数据。");
        
        string targetDirectory = string.IsNullOrWhiteSpace(descriptor.DirectoryPath) ? _configRootDir : descriptor.DirectoryPath;
        return Path.GetFullPath(Path.Combine(targetDirectory, descriptor.FileName));
    }
    
    private static string BuildDocument(MicroClawConfigTypeDescriptor descriptor, object value)
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
}