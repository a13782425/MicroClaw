using DotNetEnv;
namespace MicroClaw.Configuration;
public static class MicroClawConfig
{
    private static YamlConfigStore? _store;
    private static int _initialized;
    
    private static string? _homeDir;
    /// <summary>
    /// 获取 MicroClaw 运行主目录，默认为当前工作目录下的 ".microclaw" 子目录，可通过环境变量 "MICROCLAW_HOME" 覆盖。
    /// </summary>
    public static string? HomeDir
    {
        get
        {
            EnsureInitialized();
            return _homeDir;
        }
    }
    
    /// <summary>
    /// 获取 YAML 默认的配置文件目录，默认为 <see cref="HomeDir"/> 下的 "config" 子目录。
    /// </summary>
    public static string ConfigDir
    {
        get
        {
            EnsureInitialized();
            return Path.Combine(_homeDir!, "config");
        }
    }
    /// <summary>
    /// 获取工作目录下的 "workspace" 子目录路径，供运行时数据、技能、会话等使用。
    /// </summary>
    public static string WorkspaceDir
    {
        get
        {
            EnsureInitialized();
            return Path.Combine(_homeDir!, "workspace");
        }
    }
    
    /// <summary>
    /// Gets a strongly typed options instance.
    /// </summary>
    public static T Get<T>() where T : class, IMicroClawConfigOptions, new()
    {
        EnsureInitialized();
        return _store!.Get<T>()!;
    }
    
    /// <summary>
    /// 热更新内存中的配置实例，并同步写回对应的 YAML 文件。
    /// 需在 <see cref="Initialize"/> 之后调用；线程安全（内部串行化缓存读写与写盘）。
    /// </summary>
    public static void Save<T>(T value) where T : class, IMicroClawConfigOptions, new()
    {
        ArgumentNullException.ThrowIfNull(value);
        EnsureInitialized();
        _store!.Save(value);
    }
    
    public static void Delete<T>() where T : class, IMicroClawConfigOptions, new()
    {
        EnsureInitialized();
        _store!.Delete<T>();
    }
    
    /// <summary>
    /// 初始化配置系统。必须在应用启动时调用一次，重复调用将抛出异常。
    /// </summary>
    public static void Initialize()
    {
        _homeDir = Env.GetString(ConfigDefine.MICROCLAW_HOME, ".microclaw");
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
            throw new InvalidOperationException("MicroClawConfig.Initialize() 不可重复调用。");
        
        try
        {
            _store = new YamlConfigStore(ConfigDir);
        }
        catch
        {
            _homeDir = null;
            _store = null;
            Interlocked.Exchange(ref _initialized, 0);
            throw;
        }
    }
    
    private static void EnsureInitialized()
    {
        if (_store is null || _homeDir is null)
            throw new InvalidOperationException("MicroClawConfig 尚未初始化，请先调用 MicroClawConfig.Initialize()。");
    }
}