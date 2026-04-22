
namespace MicroClaw.Configuration;

/// <summary>
/// 某个情绪事件触发时，四个维度的加减量配置。
/// <para><c>null</c> 表示该维度不受此事件影响（保持不变）。正数为加分，负数为减分。</para>
/// </summary>
public sealed class EmotionDeltaOptions
{
    /// <summary>警觉度变化量（正加负减，null=不变）。</summary>
    [YamlMember(Alias = "alertness", Description = "警觉度变化量，正数增加，负数减少。")]
    public int? Alertness { get; set; }

    /// <summary>心情变化量（正加负减，null=不变）。</summary>
    [YamlMember(Alias = "mood", Description = "心情变化量，正数增加，负数减少。")]
    public int? Mood { get; set; }

    /// <summary>好奇心变化量（正加负减，null=不变）。</summary>
    [YamlMember(Alias = "curiosity", Description = "好奇心变化量，正数增加，负数减少。")]
    public int? Curiosity { get; set; }

    /// <summary>信心变化量（正加负减，null=不变）。</summary>
    [YamlMember(Alias = "confidence", Description = "信心变化量，正数增加，负数减少。")]
    public int? Confidence { get; set; }
}
