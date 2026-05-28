using MicroClaw.Core.Logging;
using Serilog.Events;

namespace MicroClaw.Desktop.Modules;

/// <summary>将 Serilog 适配为 <see cref="IMicroLoggerFactory"/> 以启动 MicroRuntime。</summary>
public sealed class SerilogMicroLoggerFactory : IMicroLoggerFactory
{
    private readonly Serilog.ILogger _root;
    
    public SerilogMicroLoggerFactory(Serilog.ILogger rootLogger)
    {
        _root = rootLogger;
    }
    
    public IMicroLogger CreateLogger(string categoryName)
        => new SerilogMicroLogger(_root.ForContext("SourceContext", categoryName));
    
    public IMicroLogger CreateLogger(Type categoryType)
        => new SerilogMicroLogger(_root.ForContext(categoryType));
    
    private sealed class SerilogMicroLogger(Serilog.ILogger logger) : IMicroLogger
    {
        public bool IsEnabled(MicroLogLevel level)
            => logger.IsEnabled(ToLevel(level));
        
        public void Log(MicroLogLevel level, Exception? exception, string messageTemplate, params object?[] args)
        {
            var lvl = ToLevel(level);
            if (!logger.IsEnabled(lvl)) return;
            logger.Write(lvl, exception, messageTemplate, args);
        }
        
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        
        private static LogEventLevel ToLevel(MicroLogLevel level) => level switch
        {
            MicroLogLevel.Trace       => LogEventLevel.Verbose,
            MicroLogLevel.Debug       => LogEventLevel.Debug,
            MicroLogLevel.Information => LogEventLevel.Information,
            MicroLogLevel.Warning     => LogEventLevel.Warning,
            MicroLogLevel.Error       => LogEventLevel.Error,
            MicroLogLevel.Critical    => LogEventLevel.Fatal,
            _                         => LogEventLevel.Verbose,
        };
    }
}