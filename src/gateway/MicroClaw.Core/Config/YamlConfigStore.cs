using System.Collections.Concurrent;
using System.Reflection;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MicroClaw.Configuration;
/// <summary>
/// 基于 YAML 文件的配置存储，支持按配置节读写、内存缓存、模板回退以及写时备份。
/// 每个 <see cref="YamlConfigStore"/> 绑定一个配置根目录，所有 YAML 文件均位于该目录下。
/// </summary>
public sealed class YamlConfigStore
{
    /// <summary>
    /// 用于序列化配置对象的 YAML 序列化器，使用下划线命名约定并保留默认值。
    /// </summary>
    private static readonly ISerializer Serializer = new SerializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).ConfigureDefaultValuesHandling(DefaultValuesHandling.Preserve).Build();
    
    /// <summary>
    /// 用于反序列化配置的 YAML 反序列化器，使用下划线命名约定并忽略未匹配的属性。
    /// </summary>
    private static readonly IDeserializer Deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).IgnoreUnmatchedProperties().Build();
    
    /// <summary>
    /// 配置文件所在的根目录路径。
    /// </summary>
    private readonly string _configRootDir;
    
    /// <summary>
    /// 已加载配置对象的内存缓存，键为文件完整路径。
    /// </summary>
    private readonly ConcurrentDictionary<string, object> _cache = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// 类型描述符缓存，用于避免重复反射解析 <see cref="MicroClawYamlConfigAttribute"/> 元数据。
    /// </summary>
    private readonly ConcurrentDictionary<string, MicroClawConfigTypeDescriptor> _descriptors = new(StringComparer.OrdinalIgnoreCase);
    
    /// <summary>
    /// 用于双重检查锁定的描述符缓存同步对象。
    /// </summary>
    private readonly object _descriptorCacheLock = new();
    
    /// <summary>
    /// 初始化一个新的 <see cref="YamlConfigStore"/> 实例。
    /// </summary>
    /// <param name="configRootDir">存放所有 YAML 配置文件的根目录，不能为空。</param>
    public YamlConfigStore(string configRootDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configRootDir);
        _configRootDir = configRootDir;
    }
    /// <summary>
    /// 获取指定类型的配置实例。优先从内存缓存返回，其次从 YAML 文件读取，
    /// 若文件或配置节不存在则通过模板回退创建默认配置并自动落盘。
    /// </summary>
    /// <typeparam name="T">配置选项类型，必须实现 <see cref="IMicroClawConfigOptions"/> 并标注 <see cref="MicroClawYamlConfigAttribute"/>，且具有无参构造函数。</typeparam>
    /// <param name="fileName">自定义 YAML 文件名（可选），覆盖特性中声明的文件名。</param>
    /// <param name="directoryPath">相对于配置根目录的子目录（可选）。</param>
    /// <returns>配置实例；若类型未声明文件名则返回 <c>null</c>。</returns>
    public T? Get<T>(string? fileName = null, string? directoryPath = null) where T : class, IMicroClawConfigOptions, new()
    {
        MicroClawConfigTypeDescriptor descriptor = CreateDescriptor<T>(directoryPath, fileName);
        
        // 未声明文件名则无法读取/写入文件，返回 null
        if (string.IsNullOrWhiteSpace(descriptor.FileName))
            return null;
        
        string filePath = ResolveFilePath(descriptor);
        
        // 命中内存缓存则直接返回
        if (_cache.TryGetValue(filePath, out object? cached))
            return (T)cached;
        
        T? result = null;
        
        // 文件存在时读取并反序列化指定配置节
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
            // 反序列化成功，缓存并返回
            _cache[filePath] = result;
            return result;
        }
        
        // 未找到文件或配置节 — 使用回退策略
        T placeholder = new T();
        if (placeholder is IMicroClawConfigTemplate templateProvider)
        {
            // 类型实现了模板接口，使用默认模板并落盘
            IMicroClawConfigOptions template = templateProvider.CreateDefaultTemplate() ?? throw new InvalidOperationException($"配置类型 {typeof(T).Name} 的默认模板不能为空。");
            if (template is not T typedTemplate)
                throw new InvalidOperationException($"配置类型 {typeof(T).Name} 的默认模板实例类型必须与 {typeof(T).Name} 兼容。");
            Save(typedTemplate, fileName, directoryPath);
            return typedTemplate;
        }
        
        // 无模板支持，返回默认构造的新实例（不入缓存也不落盘）
        return placeholder;
    }
    
    
    /// <summary>
    /// 保存配置实例到对应的 YAML 文件。写入前会备份已有文件（.bak），
    /// 成功后更新内存缓存。
    /// </summary>
    /// <typeparam name="T">配置选项类型。</typeparam>
    /// <param name="value">要保存的配置实例，不能为空。</param>
    /// <param name="fileName">自定义文件名（可选）。</param>
    /// <param name="directoryPath">子目录路径（可选）。</param>
    /// <returns>传入的配置实例。</returns>
    public T Save<T>(T value, string? fileName = null, string? directoryPath = null) where T : class, IMicroClawConfigOptions, new()
    {
        ArgumentNullException.ThrowIfNull(value);
        
        MicroClawConfigTypeDescriptor descriptor = CreateDescriptor<T>(directoryPath, fileName);
        string filePath = ResolveFilePath(descriptor);
        string yaml = BuildDocument(descriptor, value);
        
        WriteAllTextWithBackup(filePath, yaml);
        _cache[filePath] = value;
        return value;
    }
    
    
    /// <summary>
    /// 删除指定类型对应的 YAML 配置文件。删除前会创建 .bak 备份，
    /// 同时清除对应的内存缓存项。
    /// </summary>
    /// <typeparam name="T">配置选项类型。</typeparam>
    /// <param name="fileName">自定义文件名（可选）。</param>
    /// <param name="directoryPath">子目录路径（可选）。</param>
    /// <returns>删除成功返回 <c>true</c>；文件不存在或类型未声明文件名则返回 <c>false</c>。</returns>
    public bool Delete<T>(string? fileName = null, string? directoryPath = null) where T : class
    {
        MicroClawConfigTypeDescriptor descriptor = CreateDescriptor<T>(directoryPath, fileName);
        
        if (string.IsNullOrWhiteSpace(descriptor.FileName))
            return false;
        
        string filePath = ResolveFilePath(descriptor);
        if (!File.Exists(filePath))
            return false;
        
        // 带重试的文件删除，应对并发文件锁定
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
    
    
    /// <summary>
    /// 为目标类型创建（或从缓存获取）<see cref="MicroClawConfigTypeDescriptor"/>。
    /// 该方法执行反射验证和文件名/路径解析，并使用双重检查锁定保证线程安全。
    /// </summary>
    private MicroClawConfigTypeDescriptor CreateDescriptor<T>(string? directoryPath, string? fileName) where T : class
    {
        Type valueType = typeof(T);
        bool implementsContract = typeof(IMicroClawConfigOptions).IsAssignableFrom(valueType);
        MicroClawYamlConfigAttribute? metadata = valueType.GetCustomAttribute<MicroClawYamlConfigAttribute>(inherit: false);
        
        // 验证类型合法性
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
        
        // 解析配置节键
        string sectionKey = metadata.SectionKey?.Trim() ?? valueType.Name.ToLower();
        if (string.IsNullOrWhiteSpace(sectionKey))
            throw new InvalidOperationException($"配置类型 {valueType.Name} 的 SectionKey 不能为空。");
        
        // 解析文件名：优先级 显式参数 > 特性 > 类型名小写
        string? resolvedFileName = NormalizeNullable(fileName) ?? NormalizeNullable(metadata.FileName);
        if (string.IsNullOrWhiteSpace(resolvedFileName))
            resolvedFileName = valueType.Name.ToLower() + ".yaml";
        
        string targetDir = ResolveDirectoryPath(directoryPath) ?? _configRootDir;
        string cacheKey = Path.GetFullPath(Path.Combine(targetDir, resolvedFileName));
        
        // 双重检查锁定：先无锁读，失败后加锁创建
        lock (_descriptorCacheLock)
        {
            if (_descriptors.TryGetValue(cacheKey, out MicroClawConfigTypeDescriptor? cachedDescriptor))
                return cachedDescriptor;
            MicroClawConfigTypeDescriptor descriptor = new MicroClawConfigTypeDescriptor(valueType, sectionKey, resolvedFileName, targetDir, NormalizeNullable(metadata.HeaderComment));
            // 检查是否在加锁期间已被其他线程抢先创建
            if (_descriptors.TryGetValue(cacheKey, out MicroClawConfigTypeDescriptor? racedDescriptor))
                return racedDescriptor;
            _descriptors[cacheKey] = descriptor;
            return descriptor;
        }
    }
    
    /// <summary>
    /// 解析子目录路径
    /// </summary>
    private string? ResolveDirectoryPath(string? explicitDirectoryPath)
    {
        string? candidate = NormalizeNullable(explicitDirectoryPath);
        return candidate is null ? null : Path.GetFullPath(Path.Combine(_configRootDir, candidate));
    }
    
    /// <summary>
    /// 根据描述符拼接文件的完整绝对路径。
    /// </summary>
    private string ResolveFilePath(MicroClawConfigTypeDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.FileName))
            throw new InvalidOperationException($"配置类型 {descriptor.YamlConfigType.Name} 缺少可落盘的 FileName 元数据。");
        
        string targetDirectory = string.IsNullOrWhiteSpace(descriptor.DirectoryPath) ? _configRootDir : descriptor.DirectoryPath;
        return Path.GetFullPath(Path.Combine(targetDirectory, descriptor.FileName));
    }
    
    /// <summary>
    /// 构建完整的 YAML 文档内容。将配置值包装在以 <see cref="MicroClawConfigTypeDescriptor.SectionKey"/>
    /// 为键的字典中，如有头部注释则前置以 # 开头的注释行。
    /// </summary>
    private static string BuildDocument(MicroClawConfigTypeDescriptor descriptor, object value)
    {
        var wrapper = new Dictionary<string, object> { [descriptor.SectionKey] = value };
        
        string body = Serializer.Serialize(wrapper);
        
        if (string.IsNullOrWhiteSpace(descriptor.HeaderComment))
            return body;
        
        // 将多行头部注释转换为 YAML 注释格式（每行以 # 开头）
        IEnumerable<string> commentLines = descriptor.HeaderComment.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(static line => string.IsNullOrWhiteSpace(line) ? "#" : $"# {line}");
        
        return string.Join(Environment.NewLine, commentLines) + Environment.NewLine + Environment.NewLine + body;
    }
    
    /// <summary>
    /// 从 YAML 内容中提取指定配置节键对应的子树并重新序列化。
    /// 当 YAML 文档根节点为映射时，查找键名匹配的子节点并返回其独立 YAML 片段。
    /// </summary>
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
    
    /// <summary>
    /// 将单个 YAML 节点序列化为字符串。
    /// </summary>
    private static string SerializeNode(YamlNode node)
    {
        var stream = new YamlStream(new YamlDocument(node));
        using var writer = new StringWriter();
        stream.Save(writer, assignAnchors: false);
        return writer.ToString();
    }
    
    /// <summary>
    /// 安全写入文件：先创建目录，再备份已有文件（.bak），然后写入新内容。
    /// 写入失败时重试最多 5 次以应对并发文件锁定。
    /// </summary>
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
    
    /// <summary>
    /// 规范化可空字符串：空白字符串视为 <c>null</c>，否则去除首尾空格。
    /// </summary>
    private static string? NormalizeNullable(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}