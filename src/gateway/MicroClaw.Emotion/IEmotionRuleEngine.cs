namespace MicroClaw.Emotion;

/// <summary>
/// 情绪触发规则引擎：根据事件类型计算应施加的 <see cref="EmotionDelta"/>，
/// 或直接将当前 <see cref="EmotionState"/> 应用规则后返回新状态。
/// </summary>
public interface IEmotionRuleEngine
{
    /// <summary>
    /// 返回指定事件类型对应的合并情绪增减量。
    /// 若无匹配规则，返回 <see cref="EmotionDelta.Zero"/>。
    /// 若同一事件有多条规则，各增减量依次 Merge 叠加。
    /// </summary>
    /// <param name="eventType">触发事件类型。</param>
    EmotionDelta GetDelta(EmotionEventType eventType);

    /// <summary>
    /// 将规则施加于当前情绪状态，返回新的 <see cref="EmotionState"/>（不可变）。
    /// </summary>
    /// <param name="current">当前情绪状态。</param>
    /// <param name="eventType">触发事件类型。</param>
    EmotionState Evaluate(EmotionState current, EmotionEventType eventType);
}
