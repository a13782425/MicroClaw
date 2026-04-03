namespace MicroClaw.Pet.Emotion;

/// <summary>
/// <see cref="EmotionRuleEngine"/> 的配置选项。
/// </summary>
public sealed class EmotionRuleEngineOptions
{
    /// <summary>
    /// 是否启用内置默认规则。默认 <c>true</c>。
    /// 设为 <c>false</c> 时仅使用 <see cref="CustomRules"/> 中的规则。
    /// </summary>
    public bool UseDefaultRules { get; set; } = true;

    /// <summary>
    /// 自定义规则列表。与内置规则共存时，同一事件类型的所有规则均会 Merge 叠加。
    /// </summary>
    public IList<EmotionRule> CustomRules { get; set; } = [];
}
