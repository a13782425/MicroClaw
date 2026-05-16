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
    private static YamlConfigStore? _store;
    private static string? _configDir;
    private static int _initialized;
    
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
        return _store!.Get<T>()!;
    }
    
    /// <summary>
    /// 热更新内存中的配置实例，并同步写回对应的 YAML 文件。
    /// 需在 <see cref="Initialize"/> 之后调用；线程安全（内部串行化缓存读写与写盘）。
    /// </summary>
    public static void Save<T>(T value) where T : class, new()
    {
        ArgumentNullException.ThrowIfNull(value);
        
        EnsureInitialized();
        _store!.Save(value);
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
        }
        catch
        {
            _configuration = null;
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
        _store = null;
        _configDir = null;
        Interlocked.Exchange(ref _initialized, 0);
    }

    
    private static void EnsureInitialized()
    {
        if (_configuration is null || _store is null || _configDir is null)
            throw new InvalidOperationException("MicroClawConfig 尚未初始化，请先调用 MicroClawConfig.Initialize()。");
    }
}