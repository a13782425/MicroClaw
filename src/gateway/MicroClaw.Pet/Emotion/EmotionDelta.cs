namespace MicroClaw.Pet.Emotion;

/// <summary>
/// 情绪增减量，描述一次事件对四个情绪维度的影响。
/// <list type="bullet">
///   <item>正数表示该维度增加，负数表示减少，0 表示不变。</item>
///   <item>施加后各维度自动 Clamp 到 [0, 100]，见 <see cref="EmotionState.Apply"/>。</item>
/// </list>
/// </summary>
/// <param name="Alertness">警觉度变化量。</param>
/// <param name="Mood">心情变化量。</param>
/// <param name="Curiosity">好奇心变化量。</param>
/// <param name="Confidence">信心变化量。</param>
public sealed record EmotionDelta(
    int Alertness = 0,
    int Mood = 0,
    int Curiosity = 0,
    int Confidence = 0)
{
    /// <summary>不产生任何变化的零增减量。</summary>
    public static readonly EmotionDelta Zero = new();

    /// <summary>
    /// 将两个增减量合并为一个（各维度相加）。
    /// </summary>
    public EmotionDelta Merge(EmotionDelta other) => new(
        Alertness + other.Alertness,
        Mood + other.Mood,
        Curiosity + other.Curiosity,
        Confidence + other.Confidence);

    /// <summary>将所有维度按 <paramref name="factor"/> 等比缩放（取整）。</summary>
    public EmotionDelta Scale(double factor) => new(
        (int)(Alertness * factor),
        (int)(Mood * factor),
        (int)(Curiosity * factor),
        (int)(Confidence * factor));
}
