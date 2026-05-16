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
    private static readonly IConfiguration EmptyConfiguration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();
    
    private static MicroClawConfigEnv? _env;
    private static IConfiguration? _configuration;
    private static ConcurrentDictionary<Type, Lazy<object>>? _options;
    private static YamlConfigStore? _store;
    private static string? _configDir;
    private static int _initialized;
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
        
        lock (OptionsCacheLock)
        {
            Lazy<object> lazyValue = _options!.GetOrAdd(optionType, _ => CreateBoundOptionsLazy<T>(_configuration!));
            
            try
            {
                return (T)lazyValue.Value;
            }
            catch
            {
                _options!.TryRemove(new KeyValuePair<Type, Lazy<object>>(optionType, lazyValue));
                throw;
            }
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

        lock (OptionsCacheLock)
        {
            _store!.Save(value);
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
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
            throw new InvalidOperationException("MicroClawConfig.Initialize() 不可重复调用。");
        
        try
        {
            _configuration = configuration;
            _configDir = configDir;
            _store = new YamlConfigStore(configDir);
            _options = new ConcurrentDictionary<Type, Lazy<object>>();
        }
        catch
        {
            _configuration = null;
            _options = null;
            _configDir = null;
            _store = null;
            Interlocked.Exchange(ref _initialized, 0);
            throw;
        }
    }
    
    /// <summary>
    /// 仅供测试使用：重置初始化状态。
    /// </summary>
    internal static void Reset()
    {
        _env = null;
        _configuration = null;
        _options = null;
        _store = null;
        _configDir = null;
        Interlocked.Exchange(ref _initialized, 0);
    }

    
    private static void EnsureInitialized()
    {
        if (_configuration is null || _options is null || _store is null || _configDir is null)
            throw new InvalidOperationException("MicroClawConfig 尚未初始化，请先调用 MicroClawConfig.Initialize()。");
    }
    
    private static Lazy<object> CreateBoundOptionsLazy<T>(IConfiguration configuration) where T : class, new()
    {
        return new Lazy<object>(() =>
        {
            Type optionType = typeof(T);
            T? storedInstance = _store!.Get<T>();

            MicroClawYamlConfigAttribute metadata = GetYamlMetadataOrThrow(optionType);
            string sectionKey = metadata.SectionKey.Trim();
            string? fileName = NormalizeFileName(metadata.FileName);
            T instance = storedInstance ?? new T();
            IConfigurationSection runtimeSection = configuration.GetSection(sectionKey);
            bool runtimeSectionExists = runtimeSection.Exists();
            if (runtimeSectionExists)
                YamlAwareBinder.Bind(runtimeSection, instance);
            
            if (instance is not IMicroClawConfigTemplate templateProvider)
                return instance;
            
            if (storedInstance is not null || runtimeSectionExists)
                return instance;

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidOperationException($"配置类型 {optionType.Name} 实现了 {nameof(IMicroClawConfigTemplate)}，必须显式声明 FileName。");
            }
            
            return MaterializeTemplate<T>(optionType, fileName, templateProvider);
        }, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    private static T MaterializeTemplate<T>(Type optionType, string fileName, IMicroClawConfigTemplate templateProvider) where T : class, new()
    {
        IMicroClawConfigOptions template = templateProvider.CreateDefaultTemplate() ?? throw new InvalidOperationException($"配置类型 {optionType.Name} 的默认模板不能为空。");
        
        if (template is not T typedTemplate)
        {
            throw new InvalidOperationException($"配置类型 {optionType.Name} 的默认模板实例类型必须与 {optionType.Name} 兼容。");
        }
        
        _store!.Save(typedTemplate, fileName: fileName);
        return typedTemplate;
    }

    private static MicroClawYamlConfigAttribute GetYamlMetadataOrThrow(Type optionType)
    {
        return optionType.GetCustomAttribute<MicroClawYamlConfigAttribute>(inherit: false)
               ?? throw new InvalidOperationException($"配置类型 {optionType.Name} 缺少 [MicroClawYamlConfig]。");
    }

    private static string? NormalizeFileName(string? fileName)
    {
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim();
    }
    
    private static Lazy<object> CreateValueLazy(object value)
    {
        return new Lazy<object>(() => value, LazyThreadSafetyMode.ExecutionAndPublication);
    }
}