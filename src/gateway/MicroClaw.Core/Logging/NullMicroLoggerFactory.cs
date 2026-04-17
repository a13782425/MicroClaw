namespace MicroClaw.Core.Logging;

/// <summary>始终返回 <see cref="NullMicroLogger"/> 的 no-op 工厂。</summary>
public sealed class NullMicroLoggerFactory : IMicroLoggerFactory
{
    /// <summary>全局共享单例。</summary>
    public static readonly NullMicroLoggerFactory Instance = new();

    private NullMicroLoggerFactory()
    {
    }

    /// <inheritdoc />
    public IMicroLogger CreateLogger(string categoryName) => NullMicroLogger.Instance;

    /// <inheritdoc />
    public IMicroLogger CreateLogger(Type categoryType) => NullMicroLogger.Instance;
}
