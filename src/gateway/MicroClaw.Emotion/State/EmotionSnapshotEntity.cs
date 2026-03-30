namespace MicroClaw.Emotion;

/// <summary>
/// 情绪状态快照的持久化实体，存储于 <c>emotion_snapshots</c> 表。
/// 每次调用 <see cref="IEmotionStore.SaveAsync"/> 写入一条记录，支持历史曲线查询。
/// </summary>
public sealed class EmotionSnapshotEntity
{
    /// <summary>主键（自增整型）。</summary>
    public long Id { get; set; }

    /// <summary>所属 Agent 的唯一标识符。</summary>
    public string AgentId { get; set; } = string.Empty;

    /// <summary>警觉度，值域 [0, 100]。</summary>
    public int Alertness { get; set; }

    /// <summary>心情，值域 [0, 100]。</summary>
    public int Mood { get; set; }

    /// <summary>好奇心，值域 [0, 100]。</summary>
    public int Curiosity { get; set; }

    /// <summary>信心，值域 [0, 100]。</summary>
    public int Confidence { get; set; }

    /// <summary>记录时间（Unix 毫秒时间戳）。</summary>
    public long RecordedAtMs { get; set; }

    /// <summary>
    /// 将当前实体转换为不可变 <see cref="EmotionState"/>。
    /// </summary>
    public EmotionState ToEmotionState() =>
        new(alertness: Alertness, mood: Mood, curiosity: Curiosity, confidence: Confidence);
}
