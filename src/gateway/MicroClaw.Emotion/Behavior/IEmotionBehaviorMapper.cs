namespace MicroClaw.Emotion;

/// <summary>
/// 情绪状态 → 行为模式映射器：根据当前 <see cref="EmotionState"/> 决定应使用的
/// <see cref="BehaviorProfile"/>（含 Temperature / TopP / SystemPromptSuffix）。
/// </summary>
public interface IEmotionBehaviorMapper
{
    /// <summary>
    /// 根据当前情绪状态返回对应的行为模式参数配置。
    /// </summary>
    /// <param name="state">当前情绪状态快照。</param>
    /// <returns>匹配的 <see cref="BehaviorProfile"/>，不会返回 null。</returns>
    BehaviorProfile GetProfile(EmotionState state);
}
