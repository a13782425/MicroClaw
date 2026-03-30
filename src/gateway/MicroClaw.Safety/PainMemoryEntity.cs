namespace MicroClaw.Safety;

/// <summary>
/// 痛觉记忆的持久化实体，存储于 <c>pain_memories</c> 表。
/// </summary>
public sealed class PainMemoryEntity
{
    /// <summary>主键（32 位十六进制 GUID 字符串）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>所属 Agent 的唯一标识符。</summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>触发点描述。</summary>
    public string TriggerDescription { get; set; } = string.Empty;

    /// <summary>后果描述。</summary>
    public string ConsequenceDescription { get; set; } = string.Empty;

    /// <summary>规避策略。</summary>
    public string AvoidanceStrategy { get; set; } = string.Empty;

    /// <summary>严重度（整型存储，对应 <see cref="PainSeverity"/> 枚举值）。</summary>
    public PainSeverity Severity { get; set; }

    /// <summary>累计发生次数。</summary>
    public int OccurrenceCount { get; set; }

    /// <summary>最近发生时间（Unix 毫秒时间戳）。</summary>
    public long LastOccurredAtMs { get; set; }

    /// <summary>首次记录时间（Unix 毫秒时间戳）。</summary>
    public long CreatedAtMs { get; set; }
}
