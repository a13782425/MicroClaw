using Microsoft.Extensions.Configuration;

namespace MicroClaw.Configuration;

/// <summary>
/// MicroClaw 配置静态门面。启动时调用 <see cref="Initialize"/> 一次完成初始化，
/// 之后通过 <see cref="Get{T}"/> 获取强类型配置，通过 <see cref="Env"/> 访问环境变量和路径。
/// </summary>
public static class MicroClawConfig
{
    private static MicroClawConfigEnv? _env;
    private static Dictionary<Type, object>? _options;
    private static int _initialized;

    /// <summary>
    /// 已注册的 Options 类型到配置段名称的映射。
    /// </summary>
    private static readonly Dictionary<Type, string> SectionMap = new()
    {
        [typeof(AuthOptions)] = "auth",
        [typeof(SkillOptions)] = "skills",
        [typeof(FileToolsOptions)] = "filesystem",
        [typeof(AgentOptions)] = "agent",
        [typeof(RagOptions)] = "rag",
        [typeof(SandboxOptions)] = "sandbox",
        [typeof(EmotionOptions)] = "emotion",
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
    /// 初始化配置系统。必须在应用启动时调用一次，重复调用将抛出异常。
    /// </summary>
    /// <param name="configuration">ASP.NET Core 配置根对象。</param>
    public static void Initialize(IConfiguration configuration)
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
            throw new InvalidOperationException("MicroClawConfig.Initialize() 不可重复调用。");

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
        Interlocked.Exchange(ref _initialized, 0);
    }
}
