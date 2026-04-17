using MicroClaw.Core.Logging;
using MsLogging = Microsoft.Extensions.Logging;

namespace MicroClaw.Logging;

/// <summary>
/// 把 <see cref="MsLogging.ILoggerFactory"/>（在本项目中由 Serilog 承接）适配为 <see cref="IMicroLoggerFactory"/>，
/// 供 <c>MicroClaw.Core</c> 中的生命周期节点通过 <see cref="MicroLogger.Factory"/> 使用。
/// </summary>
internal sealed class MelMicroLoggerFactory : IMicroLoggerFactory
{
    private readonly MsLogging.ILoggerFactory _inner;

    public MelMicroLoggerFactory(MsLogging.ILoggerFactory inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public IMicroLogger CreateLogger(string categoryName)
        => new MelMicroLogger(_inner.CreateLogger(categoryName));

    /// <inheritdoc />
    public IMicroLogger CreateLogger(Type categoryType)
        => new MelMicroLogger(_inner.CreateLogger(categoryType));

    /// <summary>把 <see cref="MsLogging.ILogger"/> 包装成 <see cref="IMicroLogger"/>。</summary>
    private sealed class MelMicroLogger : IMicroLogger
    {
        private readonly MsLogging.ILogger _inner;

        public MelMicroLogger(MsLogging.ILogger inner) => _inner = inner;

        public bool IsEnabled(MicroLogLevel level) => _inner.IsEnabled(Map(level));

        public void Log(MicroLogLevel level, Exception? exception, string messageTemplate, params object?[] args)
            => _inner.Log(Map(level), exception, messageTemplate, args);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => _inner.BeginScope(state);

        private static MsLogging.LogLevel Map(MicroLogLevel level) => level switch
        {
            MicroLogLevel.Trace => MsLogging.LogLevel.Trace,
            MicroLogLevel.Debug => MsLogging.LogLevel.Debug,
            MicroLogLevel.Information => MsLogging.LogLevel.Information,
            MicroLogLevel.Warning => MsLogging.LogLevel.Warning,
            MicroLogLevel.Error => MsLogging.LogLevel.Error,
            MicroLogLevel.Critical => MsLogging.LogLevel.Critical,
            _ => MsLogging.LogLevel.None,
        };
    }
}
