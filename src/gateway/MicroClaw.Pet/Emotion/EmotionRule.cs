namespace MicroClaw.Pet.Emotion;

/// <summary>
/// 将一个事件类型映射到对应情绪增减量的规则条目。
/// </summary>
/// <param name="EventType">触发该规则的事件类型。</param>
/// <param name="Delta">事件触发后应施加的情绪增减量。</param>
/// <param name="Description">可选描述，说明该规则的语义（便于调试/日志）。</param>
public sealed record EmotionRule(
    EmotionEventType EventType,
    EmotionDelta Delta,
    string Description = "");
