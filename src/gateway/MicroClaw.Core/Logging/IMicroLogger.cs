namespace MicroClaw.Core.Logging;

/// <summary>MicroClaw 内部统一日志抽象，API 风格对齐 Microsoft.Extensions.Logging.ILogger。</summary>
public interface IMicroLogger
{
    /// <summary>指定级别的日志是否会被写出。</summary>
    bool IsEnabled(MicroLogLevel level);

    /// <summary>按结构化模板写入一条日志。</summary>
    void Log(MicroLogLevel level, Exception? exception, string messageTemplate, params object?[] args);

    /// <summary>开启一个日志作用域，返回 null 表示宿主不支持作用域。</summary>
    IDisposable? BeginScope<TState>(TState state) where TState : notnull;
}
