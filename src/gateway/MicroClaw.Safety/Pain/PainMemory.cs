using MicroClaw.Infrastructure;

namespace MicroClaw.Safety;

/// <summary>
/// 不可变的痛觉记忆条目，记录 Agent 历史上经历的失败触发点及其规避策略。
/// <para>
/// 实例不可变；更新时请通过 <see cref="WithIncrement"/> 产生新实例。
/// 使用 <see cref="Create"/> 工厂方法创建新记忆，使用 <see cref="FromEntity"/> 从持久层重建。
/// </para>
/// </summary>
public sealed record PainMemory
{
    /// <summary>记忆的唯一标识（32 位十六进制字符串）。</summary>
    public string Id { get; init; }

    /// <summary>所属 Agent 的唯一标识符。</summary>
    public string AgentId { get; init; }

    /// <summary>触发点描述：什么操作/情境引发此痛觉。</summary>
    public string TriggerDescription { get; init; }

    /// <summary>后果描述：触发后发生了什么不良后果。</summary>
    public string ConsequenceDescription { get; init; }

    /// <summary>规避策略：下次应如何避免重蹈覆辙。</summary>
    public string AvoidanceStrategy { get; init; }

    /// <summary>严重度等级。</summary>
    public PainSeverity Severity { get; init; }

    /// <summary>累计发生次数（初始值为 1）。</summary>
    public int OccurrenceCount { get; init; }

    /// <summary>最近发生时间（Unix 毫秒时间戳）。</summary>
    public long LastOccurredAtMs { get; init; }

    /// <summary>首次记录时间（Unix 毫秒时间戳）。</summary>
    public long CreatedAtMs { get; init; }

    private PainMemory(
        string id,
        string agentId,
        string triggerDescription,
        string consequenceDescription,
        string avoidanceStrategy,
        PainSeverity severity,
        int occurrenceCount,
        long lastOccurredAtMs,
        long createdAtMs)
    {
        Id = id;
        AgentId = agentId;
        TriggerDescription = triggerDescription;
        ConsequenceDescription = consequenceDescription;
        AvoidanceStrategy = avoidanceStrategy;
        Severity = severity;
        OccurrenceCount = occurrenceCount;
        LastOccurredAtMs = lastOccurredAtMs;
        CreatedAtMs = createdAtMs;
    }

    /// <summary>
    /// 创建一条新的痛觉记忆（发生次数设为 1，时间戳为当前 UTC 时间）。
    /// </summary>
    /// <param name="agentId">所属 Agent 的唯一标识符。</param>
    /// <param name="triggerDescription">触发点描述。</param>
    /// <param name="consequenceDescription">后果描述。</param>
    /// <param name="avoidanceStrategy">规避策略。</param>
    /// <param name="severity">严重度等级。</param>
    public static PainMemory Create(
        string agentId,
        string triggerDescription,
        string consequenceDescription,
        string avoidanceStrategy,
        PainSeverity severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(consequenceDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(avoidanceStrategy);

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new PainMemory(
            id: Guid.NewGuid().ToString("N"),
            agentId: agentId,
            triggerDescription: triggerDescription,
            consequenceDescription: consequenceDescription,
            avoidanceStrategy: avoidanceStrategy,
            severity: severity,
            occurrenceCount: 1,
            lastOccurredAtMs: now,
            createdAtMs: now);
    }

    /// <summary>
    /// 返回发生次数 +1、最近发生时间更新为当前 UTC 的新实例（原实例不变）。
    /// </summary>
    public PainMemory WithIncrement() => this with
    {
        OccurrenceCount = OccurrenceCount + 1,
        LastOccurredAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    };

    /// <summary>
    /// 从数据库实体重建 <see cref="PainMemory"/>（内部使用）。
    /// </summary>
    internal static PainMemory FromEntity(PainMemoryEntity e) => new(
        id: e.Id,
        agentId: e.AgentId,
        triggerDescription: e.TriggerDescription,
        consequenceDescription: e.ConsequenceDescription,
        avoidanceStrategy: e.AvoidanceStrategy,
        severity: e.Severity,
        occurrenceCount: e.OccurrenceCount,
        lastOccurredAtMs: e.LastOccurredAtMs,
        createdAtMs: e.CreatedAtMs);
}
