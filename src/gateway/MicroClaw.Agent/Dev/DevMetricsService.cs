using System.Collections.Concurrent;
using MicroClaw.Agent.Middleware;

namespace MicroClaw.Agent.Dev;

/// <summary>
/// <see cref="IDevMetricsService"/> 的默认实现：使用全局 <see cref="TimingCapture"/> 聚合
/// 跨请求的工具执行耗时，并维护最近 100 次 Agent 运行记录。
/// </summary>
public sealed class DevMetricsService : IDevMetricsService
{
    private readonly DateTime _startedAt = DateTime.UtcNow;
    private readonly TimingCapture _globalCapture = new();
    private readonly ConcurrentQueue<AgentRunRecord> _recentRuns = new();
    private int _totalRuns;
    private int _failedRuns;

    /// <inheritdoc/>
    public void RecordToolExecution(string toolName, long elapsedMs, bool success)
        => _globalCapture.Record(toolName, elapsedMs, success);

    /// <inheritdoc/>
    public void RecordAgentRun(string agentId, bool success, long durationMs)
    {
        Interlocked.Increment(ref _totalRuns);
        if (!success) Interlocked.Increment(ref _failedRuns);

        _recentRuns.Enqueue(new AgentRunRecord(agentId, success, durationMs, DateTime.UtcNow));

        // 只保留最近 100 次运行记录
        while (_recentRuns.Count > 100)
            _recentRuns.TryDequeue(out _);
    }

    /// <inheritdoc/>
    public DevMetricsSnapshot GetSnapshot()
    {
        IReadOnlyDictionary<string, ToolStats> raw = _globalCapture.GetSnapshot();
        var dtos = raw.ToDictionary(
            kv => kv.Key,
            kv => new ToolStatsDto(
                kv.Value.CallCount,
                kv.Value.ErrorCount,
                kv.Value.TotalElapsedMs,
                kv.Value.MaxElapsedMs,
                kv.Value.AverageElapsedMs),
            StringComparer.Ordinal);

        return new DevMetricsSnapshot(
            StartedAt: _startedAt,
            TotalAgentRuns: _totalRuns,
            FailedAgentRuns: _failedRuns,
            ToolStats: dtos,
            RecentRuns: _recentRuns.ToArray());
    }
}
