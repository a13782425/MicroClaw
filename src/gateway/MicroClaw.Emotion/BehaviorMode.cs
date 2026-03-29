namespace MicroClaw.Emotion;

/// <summary>
/// Agent 的行为模式，由当前情绪状态映射决定。
/// </summary>
public enum BehaviorMode
{
    /// <summary>
    /// 正常模式（默认平衡状态）：各维度均在中等区间，推理参数使用标准值。
    /// </summary>
    Normal,

    /// <summary>
    /// 探索模式：好奇心高且心情良好 → 倾向创意与发散，Temperature 偏高。
    /// </summary>
    Explore,

    /// <summary>
    /// 谨慎模式：警觉度高或信心低 → 保守严谨，Temperature 偏低，SystemPrompt 提示仔细验证。
    /// </summary>
    Cautious,

    /// <summary>
    /// 休息模式：警觉度低且心情低落 → 低活跃状态，Temperature 中低，SystemPrompt 提示简洁作答。
    /// </summary>
    Rest,
}
