namespace MicroClaw.Pet.Emotion;

/// <summary>
/// 携带时间戳的情绪状态历史条目，用于历史曲线查询结果。
/// </summary>
/// <param name="State">情绪状态快照。</param>
/// <param name="RecordedAtMs">记录时间（Unix 毫秒时间戳）。</param>
public sealed record EmotionSnapshot(EmotionState State, long RecordedAtMs);
