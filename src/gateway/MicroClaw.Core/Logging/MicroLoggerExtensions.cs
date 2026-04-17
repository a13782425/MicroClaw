namespace MicroClaw.Core.Logging;

/// <summary><see cref="IMicroLogger"/> 与 <see cref="IMicroLoggerFactory"/> 的便捷扩展。</summary>
public static class MicroLoggerExtensions
{
    /// <summary>按分类类型 <typeparamref name="T"/> 创建 logger。</summary>
    public static IMicroLogger CreateLogger<T>(this IMicroLoggerFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.CreateLogger(typeof(T));
    }

    /// <summary>写入 Trace 级别日志。</summary>
    public static void LogTrace(this IMicroLogger logger, string messageTemplate, params object?[] args)
        => logger.Log(MicroLogLevel.Trace, null, messageTemplate, args);

    /// <summary>写入 Trace 级别日志并附带异常。</summary>
    public static void LogTrace(this IMicroLogger logger, Exception? exception, string messageTemplate, params object?[] args)
        => logger.Log(MicroLogLevel.Trace, exception, messageTemplate, args);

    /// <summary>写入 Debug 级别日志。</summary>
    public static void LogDebug(this IMicroLogger logger, string messageTemplate, params object?[] args)
        => logger.Log(MicroLogLevel.Debug, null, messageTemplate, args);

    /// <summary>写入 Debug 级别日志并附带异常。</summary>
    public static void LogDebug(this IMicroLogger logger, Exception? exception, string messageTemplate, params object?[] args)
        => logger.Log(MicroLogLevel.Debug, exception, messageTemplate, args);

    /// <summary>写入 Information 级别日志。</summary>
    public static void LogInformation(this IMicroLogger logger, string messageTemplate, params object?[] args)
        => logger.Log(MicroLogLevel.Information, null, messageTemplate, args);

    /// <summary>写入 Information 级别日志并附带异常。</summary>
    public static void LogInformation(this IMicroLogger logger, Exception? exception, string messageTemplate, params object?[] args)
        => logger.Log(MicroLogLevel.Information, exception, messageTemplate, args);

    /// <summary>写入 Warning 级别日志。</summary>
    public static void LogWarning(this IMicroLogger logger, string messageTemplate, params object?[] args)
        => logger.Log(MicroLogLevel.Warning, null, messageTemplate, args);

    /// <summary>写入 Warning 级别日志并附带异常。</summary>
    public static void LogWarning(this IMicroLogger logger, Exception? exception, string messageTemplate, params object?[] args)
        => logger.Log(MicroLogLevel.Warning, exception, messageTemplate, args);

    /// <summary>写入 Error 级别日志。</summary>
    public static void LogError(this IMicroLogger logger, string messageTemplate, params object?[] args)
        => logger.Log(MicroLogLevel.Error, null, messageTemplate, args);

    /// <summary>写入 Error 级别日志并附带异常。</summary>
    public static void LogError(this IMicroLogger logger, Exception? exception, string messageTemplate, params object?[] args)
        => logger.Log(MicroLogLevel.Error, exception, messageTemplate, args);

    /// <summary>写入 Critical 级别日志。</summary>
    public static void LogCritical(this IMicroLogger logger, string messageTemplate, params object?[] args)
        => logger.Log(MicroLogLevel.Critical, null, messageTemplate, args);

    /// <summary>写入 Critical 级别日志并附带异常。</summary>
    public static void LogCritical(this IMicroLogger logger, Exception? exception, string messageTemplate, params object?[] args)
        => logger.Log(MicroLogLevel.Critical, exception, messageTemplate, args);
}
