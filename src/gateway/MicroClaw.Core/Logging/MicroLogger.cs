namespace MicroClaw.Core.Logging;

/// <summary>
/// 进程级别的日志工厂环境入口。宿主在启动时可替换 <see cref="Factory"/> 以把日志导向外部实现
/// （例如 Microsoft.Extensions.Logging 或 Serilog 适配器）。默认值为 <see cref="NullMicroLoggerFactory"/>。
/// </summary>
public static class MicroLogger
{
    private static IMicroLoggerFactory _factory = NullMicroLoggerFactory.Instance;

    /// <summary>当前正在使用的日志工厂。赋 null 时会回退到 <see cref="NullMicroLoggerFactory"/>。</summary>
    public static IMicroLoggerFactory Factory
    {
        get => _factory;
        set => _factory = value ?? NullMicroLoggerFactory.Instance;
    }

    /// <summary>使用当前工厂创建分类名来自指定类型的 logger。</summary>
    public static IMicroLogger Create(Type type) => _factory.CreateLogger(type);

    /// <summary>使用当前工厂创建分类名来自 <typeparamref name="T"/> 的 logger。</summary>
    public static IMicroLogger Create<T>() => _factory.CreateLogger(typeof(T));

    /// <summary>使用当前工厂按指定分类名创建 logger。</summary>
    public static IMicroLogger Create(string categoryName) => _factory.CreateLogger(categoryName);
}
