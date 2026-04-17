namespace MicroClaw.Core.Logging;

/// <summary>日志级别，语义与 Microsoft.Extensions.Logging.LogLevel 对齐。</summary>
public enum MicroLogLevel
{
    Trace = 0,
    Debug = 1,
    Information = 2,
    Warning = 3,
    Error = 4,
    Critical = 5,
    None = 6,
}
