using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.Middleware;

/// <summary>
/// 工具执行耗时追踪中间件。
/// 以工具函数名为键，记录每次调用的耗时、调用次数与错误次数，供日志和性能分析使用。
/// </summary>
public static class ToolExecutionTimingMiddleware
{
    /// <summary>创建新的耗时捕获器，用于一次 Agent 执行生命周期。</summary>
    public static TimingCapture CreateCapture() => new();

    /// <summary>
    /// 将一次工具调用结果记录到捕获器，并在耗时超过阈值时记录 Warning 日志。
    /// </summary>
    /// <param name="capture">当前 Agent 执行周期的捕获器。</param>
    /// <param name="toolName">工具函数名称。</param>
    /// <param name="elapsedMs">工具执行耗时（毫秒）。</param>
    /// <param name="success">本次调用是否成功。</param>
    /// <param name="logger">日志记录器。</param>
    /// <param name="slowThresholdMs">超过该阈值时记录 Warning（默认 5000ms）。</param>
    public static void Record(
        TimingCapture capture,
        string toolName,
        long elapsedMs,
        bool success,
        ILogger logger,
        long slowThresholdMs = 5_000)
    {
        capture.Record(toolName, elapsedMs, success);

        if (elapsedMs >= slowThresholdMs)
            logger.LogWarning(
                "Slow tool execution: '{Tool}' took {ElapsedMs}ms (threshold: {ThresholdMs}ms)",
                toolName, elapsedMs, slowThresholdMs);
    }
}

/// <summary>线程安全的工具执行耗时统计捕获器。</summary>
public sealed class TimingCapture
{
    private readonly ConcurrentDictionary<string, ToolStats> _stats =
        new(StringComparer.Ordinal);

    /// <summary>记录一次工具调用结果。</summary>
    public void Record(string toolName, long elapsedMs, bool success)
    {
        _stats.AddOrUpdate(
            toolName,
            _ => new ToolStats(1, success ? 0 : 1, elapsedMs, elapsedMs),
            (_, existing) => existing with
            {
                CallCount = existing.CallCount + 1,
                ErrorCount = existing.ErrorCount + (success ? 0 : 1),
                TotalElapsedMs = existing.TotalElapsedMs + elapsedMs,
                MaxElapsedMs = Math.Max(existing.MaxElapsedMs, elapsedMs)
            });
    }

    /// <summary>返回所有工具的统计快照。</summary>
    public IReadOnlyDictionary<string, ToolStats> GetSnapshot() =>
        _stats.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal);

    /// <summary>已记录统计的工具总数。</summary>
    public int ToolCount => _stats.Count;
}

/// <summary>单个工具函数的执行统计数据（不可变记录）。</summary>
public sealed record ToolStats(
    int CallCount,
    int ErrorCount,
    long TotalElapsedMs,
    long MaxElapsedMs)
{
    /// <summary>平均单次执行耗时（毫秒）。</summary>
    public double AverageElapsedMs => CallCount > 0 ? (double)TotalElapsedMs / CallCount : 0;
}
