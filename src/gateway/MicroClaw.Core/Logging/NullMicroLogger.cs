namespace MicroClaw.Core.Logging;

/// <summary>丢弃所有日志的 no-op <see cref="IMicroLogger"/> 实现。</summary>
public sealed class NullMicroLogger : IMicroLogger
{
    /// <summary>全局共享单例。</summary>
    public static readonly NullMicroLogger Instance = new();

    private NullMicroLogger()
    {
    }

    /// <inheritdoc />
    public bool IsEnabled(MicroLogLevel level) => false;

    /// <inheritdoc />
    public void Log(MicroLogLevel level, Exception? exception, string messageTemplate, params object?[] args)
    {
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
}
