namespace MicroClaw.Core.Logging;

/// <summary>用于创建 <see cref="IMicroLogger"/> 实例的工厂抽象。</summary>
public interface IMicroLoggerFactory
{
    /// <summary>按分类名创建 logger。</summary>
    IMicroLogger CreateLogger(string categoryName);

    /// <summary>按分类类型创建 logger，分类名通常取类型的 FullName。</summary>
    IMicroLogger CreateLogger(Type categoryType);
}
