using MicroClaw.Configuration;

namespace MicroClaw.Emotion;

/// <summary>
/// <see cref="EmotionBehaviorMapper"/> 的阈值与模式参数配置。
/// </summary>
public sealed class EmotionBehaviorMapperOptions
{
    // ── 判定阈值 ──

    /// <summary>
    /// 触发「谨慎」模式的警觉度下限（含）。默认 70。
    /// 当 <see cref="EmotionState.Alertness"/> &gt;= 此值时优先进入谨慎模式。
    /// </summary>
    public int CautiousAlertnessThreshold { get; set; } = 70;

    /// <summary>
    /// 触发「谨慎」模式的信心上限（含）。默认 30。
    /// 当 <see cref="EmotionState.Confidence"/> &lt;= 此值时优先进入谨慎模式。
    /// </summary>
    public int CautiousConfidenceThreshold { get; set; } = 30;

    /// <summary>
    /// 触发「探索」模式的好奇心下限（含）。默认 70。
    /// </summary>
    public int ExploreMinCuriosity { get; set; } = 70;

    /// <summary>
    /// 触发「探索」模式的心情下限（含）。默认 60。
    /// </summary>
    public int ExploreMinMood { get; set; } = 60;

    /// <summary>
    /// 触发「休息」模式的警觉度上限（含）。默认 30。
    /// </summary>
    public int RestMaxAlertness { get; set; } = 30;

    /// <summary>
    /// 触发「休息」模式的心情上限（含）。默认 40。
    /// </summary>
    public int RestMaxMood { get; set; } = 40;

    // ── 各模式推理参数（可覆盖默认值）──

    /// <summary>正常模式的推理参数。默认 <see cref="BehaviorProfile.DefaultNormal"/>。</summary>
    public BehaviorProfile NormalProfile { get; set; } = BehaviorProfile.DefaultNormal;

    /// <summary>探索模式的推理参数。默认 <see cref="BehaviorProfile.DefaultExplore"/>。</summary>
    public BehaviorProfile ExploreProfile { get; set; } = BehaviorProfile.DefaultExplore;

    /// <summary>谨慎模式的推理参数。默认 <see cref="BehaviorProfile.DefaultCautious"/>。</summary>
    public BehaviorProfile CautiousProfile { get; set; } = BehaviorProfile.DefaultCautious;

    /// <summary>休息模式的推理参数。默认 <see cref="BehaviorProfile.DefaultRest"/>。</summary>
    public BehaviorProfile RestProfile { get; set; } = BehaviorProfile.DefaultRest;

    /// <summary>
    /// 从 <see cref="EmotionOptions"/>（YAML 配置）构建 <see cref="EmotionBehaviorMapperOptions"/>。
    /// </summary>
    public static EmotionBehaviorMapperOptions FromEmotionOptions(EmotionOptions opts) => new()
    {
        CautiousAlertnessThreshold = opts.CautiousAlertnessThreshold,
        CautiousConfidenceThreshold = opts.CautiousConfidenceThreshold,
        ExploreMinCuriosity = opts.ExploreMinCuriosity,
        ExploreMinMood = opts.ExploreMinMood,
        RestMaxAlertness = opts.RestMaxAlertness,
        RestMaxMood = opts.RestMaxMood,
        NormalProfile = new BehaviorProfile(
            BehaviorMode.Normal,
            opts.NormalTemperature,
            opts.NormalTopP,
            opts.NormalSystemPromptSuffix),
        ExploreProfile = new BehaviorProfile(
            BehaviorMode.Explore,
            opts.ExploreTemperature,
            opts.ExploreTopP,
            opts.ExploreSystemPromptSuffix),
        CautiousProfile = new BehaviorProfile(
            BehaviorMode.Cautious,
            opts.CautiousTemperature,
            opts.CautiousTopP,
            opts.CautiousSystemPromptSuffix),
        RestProfile = new BehaviorProfile(
            BehaviorMode.Rest,
            opts.RestTemperature,
            opts.RestTopP,
            opts.RestSystemPromptSuffix),
    };
}
