using Microsoft.Extensions.Configuration;
using MicroClaw.Configuration.Options;

namespace MicroClaw.Configuration;

/// <summary>
/// MicroClaw 配置静态门面。启动时调用 <see cref="Initialize"/> 一次完成初始化，
/// 之后通过 <see cref="Get{T}"/> 获取强类型配置，通过 <see cref="Env"/> 访问环境变量和路径。
/// </summary>
public static class MicroClawConfig
{
    private static MicroClawConfigEnv? _env;
    private static Dictionary<Type, object>? _options;
    private static string? _configDir;
    private static int _initialized;
    private static readonly SemaphoreSlim SaveLock = new(1, 1);

    /// <summary>
    /// 已注册的 Options 类型到配置段名称的映射。
    /// </summary>
    private static readonly Dictionary<Type, string> SectionMap = new()
    {
        [typeof(AuthOptions)] = "auth",
        [typeof(SkillOptions)] = "skills",
        [typeof(FileToolsOptions)] = "filesystem",
        [typeof(AgentsOptions)] = "agents",
        [typeof(SessionsOptions)] = "sessions",
        [typeof(ProvidersOptions)] = "providers",
        [typeof(RagOptions)] = "rag",
        [typeof(SandboxOptions)] = "sandbox",
        [typeof(EmotionOptions)] = "emotion",
        [typeof(ChannelOptions)] = "channel",
    };

    /// <summary>
    /// Options 类型到对应 YAML 文件名的映射（相对于 configDir）。
    /// 只有需要运行时写回的类型才需要注册。
    /// </summary>
    private static readonly Dictionary<Type, string> FileMap = new()
    {
        [typeof(AgentsOptions)] = "agents.yaml",
        [typeof(SessionsOptions)] = "sessions.yaml",
        [typeof(ProvidersOptions)] = "providers.yaml",
        [typeof(EmotionOptions)] = "emotion.yaml",
        [typeof(SkillOptions)] = "skills.yaml",
        [typeof(RagOptions)] = "rag.yaml",
        [typeof(ChannelOptions)] = "channels.yaml",
    };

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
    /// 获取强类型配置对象。类型与配置段的映射在 <see cref="SectionMap"/> 中定义。
    /// </summary>
    public static T Get<T>() where T : class, new()
    {
        if (_options is null)
            throw new InvalidOperationException(
                "MicroClawConfig 尚未初始化，请先调用 MicroClawConfig.Initialize()。");

        if (_options.TryGetValue(typeof(T), out var value))
            return (T)value;

        throw new InvalidOperationException(
            $"配置类型 {typeof(T).Name} 未注册。请在 MicroClawConfig.SectionMap 中添加映射。");
    }

    /// <summary>
    /// Hot-update a registered options instance in memory (does NOT persist to YAML).
    /// Used when API endpoints modify config at runtime.
    /// </summary>
    public static void Update<T>(T value) where T : class, new()
    {
        if (_options is null)
            throw new InvalidOperationException("MicroClawConfig 尚未初始化。");

        if (!SectionMap.ContainsKey(typeof(T)))
            throw new InvalidOperationException(
                $"配置类型 {typeof(T).Name} 未注册。请在 MicroClawConfig.SectionMap 中添加映射。");

        _options[typeof(T)] = value;
    }

    /// <summary>
    /// 热更新内存中的配置实例，并同步写回对应的 YAML 文件。
    /// 需在 <see cref="Initialize"/> 之后调用；线程安全（内部序列化写操作）。
    /// </summary>
    public static void Save<T>(T value) where T : class, new()
    {
        if (_configDir is null)
            throw new InvalidOperationException("MicroClawConfig 尚未初始化。");

        Update(value);

        if (!FileMap.TryGetValue(typeof(T), out string? fileName))
            throw new InvalidOperationException(
                $"配置类型 {typeof(T).Name} 未在 FileMap 中注册，无法写回文件。");

        if (!SectionMap.TryGetValue(typeof(T), out string? sectionKey))
            throw new InvalidOperationException(
                $"配置类型 {typeof(T).Name} 未在 SectionMap 中注册。");

        string filePath = Path.Combine(_configDir, fileName);
        SaveLock.Wait();
        try
        {
            YamlSectionWriter.Write(filePath, sectionKey, value);
        }
        finally
        {
            SaveLock.Release();
        }
    }

    /// <summary>
    /// 初始化配置系统。必须在应用启动时调用一次，重复调用将抛出异常。
    /// </summary>
    /// <param name="configuration">ASP.NET Core 配置根对象。</param>
    /// <param name="configDir">配置文件目录（用于 <see cref="Save{T}"/> 写回）。</param>
    public static void Initialize(IConfiguration configuration, string configDir)
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
            throw new InvalidOperationException("MicroClawConfig.Initialize() 不可重复调用。");

        _configDir = configDir;

        var options = new Dictionary<Type, object>();
        foreach (var (type, section) in SectionMap)
        {
            var instance = Activator.CreateInstance(type)!;
            configuration.GetSection(section).Bind(instance);
            options[type] = instance;
        }
        _options = options;
    }

    /// <summary>
    /// 仅供测试使用：重置初始化状态。
    /// </summary>
    internal static void Reset()
    {
        _env = null;
        _options = null;
        _configDir = null;
        Interlocked.Exchange(ref _initialized, 0);
    }
}
