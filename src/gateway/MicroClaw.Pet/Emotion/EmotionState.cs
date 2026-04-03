namespace MicroClaw.Pet.Emotion;

/// <summary>
/// Pet 的情绪状态快照：由四个独立维度组成，每个维度的取值范围为 [0, 100]。
/// <list type="bullet">
///   <item><see cref="Alertness"/>（警觉度）：0 = 极度倦怠，100 = 极度亢奋。</item>
///   <item><see cref="Mood"/>（心情）：0 = 极度低落，100 = 极度愉悦。</item>
///   <item><see cref="Curiosity"/>（好奇心）：0 = 漠然，100 = 强烈探索欲。</item>
///   <item><see cref="Confidence"/>（信心）：0 = 极度不确定，100 = 极度自信。</item>
/// </list>
/// <para>
/// 实例不可变：使用 <see cref="Apply"/> 方法产生新快照，所有维度自动 Clamp 到 [0, 100]。
/// </para>
/// </summary>
public sealed record EmotionState
{
    /// <summary>警觉度，值域 [0, 100]。</summary>
    public int Alertness { get; init; }

    /// <summary>心情，值域 [0, 100]。</summary>
    public int Mood { get; init; }

    /// <summary>好奇心，值域 [0, 100]。</summary>
    public int Curiosity { get; init; }

    /// <summary>信心，值域 [0, 100]。</summary>
    public int Confidence { get; init; }

    /// <summary>
    /// 初始化情绪状态。各维度若超出 [0, 100] 则自动 Clamp。
    /// </summary>
    public EmotionState(
        int alertness = DefaultValue,
        int mood = DefaultValue,
        int curiosity = DefaultValue,
        int confidence = DefaultValue)
    {
        Alertness = Clamp(alertness);
        Mood = Clamp(mood);
        Curiosity = Clamp(curiosity);
        Confidence = Clamp(confidence);
    }

    /// <summary>各维度的默认值（平衡状态）。</summary>
    public const int DefaultValue = 50;

    /// <summary>平衡状态的默认情绪快照（四维均为 50）。</summary>
    public static readonly EmotionState Default = new();

    /// <summary>
    /// 应用情绪增减量，返回新的 <see cref="EmotionState"/>。
    /// 各维度结果自动 Clamp 到 [0, 100]，原实例不变。
    /// </summary>
    /// <param name="delta">要施加的情绪增减量。</param>
    public EmotionState Apply(EmotionDelta delta) => new(
        alertness: Alertness + delta.Alertness,
        mood: Mood + delta.Mood,
        curiosity: Curiosity + delta.Curiosity,
        confidence: Confidence + delta.Confidence);

    /// <summary>将值 Clamp 到 [0, 100]。</summary>
    public static int Clamp(int value) => Math.Clamp(value, 0, 100);

    /// <summary>返回可读的调试字符串。</summary>
    public override string ToString() =>
        $"EmotionState {{ 警觉度={Alertness}, 心情={Mood}, 好奇心={Curiosity}, 信心={Confidence} }}";
}
