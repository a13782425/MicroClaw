namespace MicroClaw.Agent.Dev;

/// <summary>
/// 收集并聚合 Agent 执行期间的开发调试指标，供 /dev/metrics 端点对外暴露。
/// 在生产环境仍会注册以保持 DI 图完整，但调试端点仅在 Development 环境映射。
/// </summary>
public interface IDevMetricsService
{
    /// <summary>记录一次工具执行（TODO：目前无调用点，待 MicroProvider 内部工具循环回填）。</summary>
    void RecordToolExecution(string toolName, long elapsedMs, bool success);

    /// <summary>记录一次 Agent 运行完成。</summary>
    void RecordAgentRun(string agentId, bool success, long durationMs);

    /// <summary>返回当前聚合指标快照（线程安全）。</summary>
    DevMetricsSnapshot GetSnapshot();
}

/// <summary>聚合指标快照（不可变）。</summary>
public sealed record DevMetricsSnapshot(
    DateTime StartedAt,
    int TotalAgentRuns,
    int FailedAgentRuns,
    IReadOnlyDictionary<string, ToolStatsDto> ToolStats,
    IReadOnlyList<AgentRunRecord> RecentRuns);

/// <summary>单个工具函数的执行统计（DTO，不依赖 Middleware 内部类型）。</summary>
public sealed record ToolStatsDto(
    int CallCount,
    int ErrorCount,
    long TotalElapsedMs,
    long MaxElapsedMs,
    double AverageElapsedMs);

/// <summary>最近一次 Agent 运行记录。</summary>
public sealed record AgentRunRecord(
    string AgentId,
    bool Success,
    long DurationMs,
    DateTime ExecutedAt);
