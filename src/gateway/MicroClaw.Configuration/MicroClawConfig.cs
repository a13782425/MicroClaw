using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace MicroClaw.Configuration;
/// <summary>
/// MicroClaw 配置静态门面。启动时调用 <see cref="Initialize"/> 一次完成初始化，
/// 之后通过 <see cref="Get{T}"/> 获取强类型配置，通过 <see cref="Env"/> 访问环境变量和路径。
/// </summary>
public static class MicroClawConfig
{
    private static MicroClawConfigEnv? _env;
    private static IConfiguration? _configuration;
    private static ConcurrentDictionary<Type, Lazy<object>>? _options;
    private static ConcurrentDictionary<Type, MicroClawConfigTypeDescriptor>? _descriptors;
    private static string? _configDir;
    private static int _initialized;
    private static readonly object DescriptorCacheLock = new();
    private static readonly object OptionsCacheLock = new();
    
    /// <summary>
    /// 环境变量和路径访问入口。
    /// </summary>
    public static MicroClawConfigEnv Env
    {
        get
        {
            if (_env == null)
                _env = new MicroClawConfigEnv();
            return _env;
        }
    }
    
    /// <summary>
    /// Gets a strongly typed options instance.
    /// </summary>
    public static T Get<T>() where T : class, new()
    {
        EnsureInitialized();
        Type optionType = typeof(T);
        MicroClawConfigTypeDescriptor descriptor = GetDescriptorOrAdd(optionType);
        
        lock (OptionsCacheLock)
        {
            Lazy<object> lazyValue = _options!.GetOrAdd(optionType, _ => CreateBoundOptionsLazy(descriptor, _configuration!));
            
            try
            {
                return (T)lazyValue.Value;
            }
            catch
            {
                _options.TryRemove(new KeyValuePair<Type, Lazy<object>>(optionType, lazyValue));
                throw;
            }
        }
    }
    
    /// <summary>
    /// Hot-update a registered options instance in memory (does NOT persist to YAML).
    /// Used when API endpoints modify config at runtime.
    /// </summary>
    public static void Update<T>(T value) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(value);
        
        EnsureInitialized();
        Type optionType = typeof(T);
        
        _ = GetDescriptorOrAdd(optionType);
        
        lock (OptionsCacheLock)
        {
            _options![optionType] = CreateValueLazy(value);
        }
    }
    
    /// <summary>
    /// 热更新内存中的配置实例，并同步写回对应的 YAML 文件。
    /// 需在 <see cref="Initialize"/> 之后调用；线程安全（内部串行化缓存读写与写盘）。
    /// </summary>
    public static void Save<T>(T value) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(value);
        
        EnsureInitialized();
        Type optionType = typeof(T);
        
        MicroClawConfigTypeDescriptor descriptor = GetDescriptorOrAdd(optionType);
        if (!descriptor.IsWritable)
        {
            throw new InvalidOperationException($"配置类型 {optionType.Name} 未声明为可写 YAML，不能调用 Save。");
        }
        
        if (string.IsNullOrWhiteSpace(descriptor.FileName))
        {
            throw new InvalidOperationException($"配置类型 {optionType.Name} 缺少可写 YAML 的 FileName 元数据。");
        }
        
        string filePath = Path.Combine(_configDir!, descriptor.FileName);
        
        lock (OptionsCacheLock)
        {
            YamlSectionWriter.Write(filePath, descriptor.SectionKey, value);
            _options![optionType] = CreateValueLazy(value);
        }
    }
    
    /// <summary>
    /// 初始化配置系统。必须在应用启动时调用一次，重复调用将抛出异常。
    /// </summary>
    /// <param name="configuration">ASP.NET Core 配置根对象。</param>
    /// <param name="configDir">配置文件目录（用于 <see cref="Save{T}"/> 写回）。</param>
    public static void Initialize(IConfiguration configuration, string configDir)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(configDir);
        
        InitializeCore(configuration, configDir);
    }
    
    internal static int CachedDescriptorCount => _descriptors?.Count ?? 0;
    
    internal static int CachedOptionsCount => _options?.Count ?? 0;
    
    internal static bool IsDescriptorCached<T>() where T : class, new() => _descriptors?.ContainsKey(typeof(T)) ?? false;
    
    internal static bool IsOptionCached<T>() where T : class, new() => _options?.ContainsKey(typeof(T)) ?? false;
    
    /// <summary>
    /// 仅供测试使用：重置初始化状态。
    /// </summary>
    internal static void Reset()
    {
        _env = null;
        _configuration = null;
        _options = null;
        _descriptors = null;
        _configDir = null;
        Interlocked.Exchange(ref _initialized, 0);
    }
    
    private static void InitializeCore(IConfiguration configuration, string configDir)
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
            throw new InvalidOperationException("MicroClawConfig.Initialize() 不可重复调用。");
        
        try
        {
            _configuration = configuration;
            _configDir = configDir;
            _descriptors = new ConcurrentDictionary<Type, MicroClawConfigTypeDescriptor>();
            _options = new ConcurrentDictionary<Type, Lazy<object>>();
        }
        catch
        {
            _configuration = null;
            _options = null;
            _descriptors = null;
            _configDir = null;
            Interlocked.Exchange(ref _initialized, 0);
            throw;
        }
    }
    
    private static void EnsureInitialized()
    {
        if (_configuration is null || _options is null || _descriptors is null || _configDir is null)
            throw new InvalidOperationException("MicroClawConfig 尚未初始化，请先调用 MicroClawConfig.Initialize()。");
    }
    
    private static MicroClawConfigTypeDescriptor GetDescriptorOrAdd(Type optionType)
    {
        if (_descriptors!.TryGetValue(optionType, out MicroClawConfigTypeDescriptor? cachedDescriptor))
            return cachedDescriptor;
        
        lock (DescriptorCacheLock)
        {
            if (_descriptors.TryGetValue(optionType, out cachedDescriptor))
                return cachedDescriptor;
            
            MicroClawConfigTypeDescriptor descriptor = CreateDescriptor(optionType);
            EnsureNoDescriptorConflict(descriptor);
            _descriptors[optionType] = descriptor;
            return descriptor;
        }
    }
    
    private static MicroClawConfigTypeDescriptor CreateDescriptor(Type optionType)
    {
        bool implementsContract = typeof(IMicroClawConfigOptions).IsAssignableFrom(optionType);
        MicroClawYamlConfigAttribute? metadata = optionType.GetCustomAttribute<MicroClawYamlConfigAttribute>(inherit: false);
        
        if (!implementsContract && metadata is null)
        {
            throw new InvalidOperationException($"配置类型 {optionType.Name} 必须同时实现 {nameof(IMicroClawConfigOptions)} 并标注 [MicroClawYamlConfig]。");
        }
        
        if (!implementsContract)
        {
            throw new InvalidOperationException($"配置类型 {optionType.Name} 标注了 [MicroClawYamlConfig]，但未实现 {nameof(IMicroClawConfigOptions)}。");
        }
        
        if (metadata is null)
        {
            throw new InvalidOperationException($"配置类型 {optionType.Name} 实现了 {nameof(IMicroClawConfigOptions)}，但缺少 [MicroClawYamlConfig]。");
        }
        
        if (!optionType.IsClass || optionType.IsAbstract)
        {
            throw new InvalidOperationException($"配置类型 {optionType.Name} 必须是可实例化的具体 class。");
        }
        
        if (optionType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new InvalidOperationException($"配置类型 {optionType.Name} 必须提供无参构造函数以支持配置绑定。");
        }
        
        string sectionKey = metadata.SectionKey.Trim();
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            throw new InvalidOperationException($"配置类型 {optionType.Name} 的 SectionKey 不能为空。");
        }
        
        string? fileName = string.IsNullOrWhiteSpace(metadata.FileName) ? optionType.Name.ToLower() + ".yaml" : metadata.FileName.Trim();
        
        EnsureSafeFileName(optionType, fileName);
        
        if (metadata.IsWritable && string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException($"配置类型 {optionType.Name} 被标记为可写，但未声明 FileName。");
        }
        
        return new MicroClawConfigTypeDescriptor(optionType, sectionKey, fileName, metadata.IsWritable);
    }
    
    private static void EnsureNoDescriptorConflict(MicroClawConfigTypeDescriptor descriptor)
    {
        foreach (MicroClawConfigTypeDescriptor existingDescriptor in _descriptors!.Values)
        {
            if (string.Equals(existingDescriptor.SectionKey, descriptor.SectionKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"配置节 '{descriptor.SectionKey}' 被类型 {existingDescriptor.OptionsType.FullName} 和 {descriptor.OptionsType.FullName} 重复声明。");
            }
            
            if (descriptor.FileName is { } fileName && existingDescriptor.FileName is { } existingFileName && string.Equals(existingFileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"YAML 文件 '{fileName}' 被类型 {existingDescriptor.OptionsType.FullName} 和 {descriptor.OptionsType.FullName} 重复声明。");
            }
        }
    }
    
    private static void EnsureSafeFileName(Type optionType, string fileName)
    {
        string extension = Path.GetExtension(fileName);
        
        if (Path.IsPathRooted(fileName) || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || fileName.Contains(':', StringComparison.Ordinal) || fileName.EndsWith(' ') || fileName.EndsWith('.') || (!string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"配置类型 {optionType.Name} 的 FileName 必须是配置目录下安全的单个 .yaml/.yml 文件名。");
        }
    }
    
    private static Lazy<object> CreateBoundOptionsLazy(MicroClawConfigTypeDescriptor descriptor, IConfiguration configuration)
    {
        return new Lazy<object>(() =>
        {
            object instance = Activator.CreateInstance(descriptor.OptionsType)!;
            IConfigurationSection section = configuration.GetSection(descriptor.SectionKey);
            YamlAwareBinder.Bind(section, instance);

            if (instance is not IMicroClawConfigTemplate templateProvider)
                return instance;

            bool sectionMissing = !section.Exists();
            if (!sectionMissing)
                return instance;

            return MaterializeTemplate(descriptor, templateProvider);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private static object MaterializeTemplate(MicroClawConfigTypeDescriptor descriptor, IMicroClawConfigTemplate templateProvider)
    {
        IMicroClawConfigOptions template = templateProvider.CreateDefaultTemplate()
            ?? throw new InvalidOperationException($"配置类型 {descriptor.OptionsType.Name} 的默认模板不能为空。");

        if (!descriptor.OptionsType.IsInstanceOfType(template))
        {
            throw new InvalidOperationException(
                $"配置类型 {descriptor.OptionsType.Name} 的默认模板实例类型必须与 {descriptor.OptionsType.Name} 兼容。");
        }

        string filePath = GetDescriptorFilePath(descriptor);
        YamlSectionWriter.Write(filePath, descriptor.SectionKey, template);
        return template;
    }

    private static string GetDescriptorFilePath(MicroClawConfigTypeDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.FileName))
            throw new InvalidOperationException($"配置类型 {descriptor.OptionsType.Name} 缺少可落盘的 FileName 元数据。");

        return Path.Combine(_configDir!, descriptor.FileName);
    }
    
    private static Lazy<object> CreateValueLazy(object value)
    {
        return new Lazy<object>(() => value, LazyThreadSafetyMode.ExecutionAndPublication);
    }
}