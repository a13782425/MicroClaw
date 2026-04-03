namespace MicroClaw.Pet.Emotion;

/// <summary>
/// 基于阈值规则的情绪 → 行为模式映射器实现。
/// <para>
/// 判定优先级（高 → 低）：
/// <list type="number">
///   <item><b>谨慎</b>：警觉度 &gt;= <see cref="EmotionBehaviorMapperOptions.CautiousAlertnessThreshold"/>
///     或 信心 &lt;= <see cref="EmotionBehaviorMapperOptions.CautiousConfidenceThreshold"/>。</item>
///   <item><b>探索</b>：好奇心 &gt;= <see cref="EmotionBehaviorMapperOptions.ExploreMinCuriosity"/>
///     且 心情 &gt;= <see cref="EmotionBehaviorMapperOptions.ExploreMinMood"/>。</item>
///   <item><b>休息</b>：警觉度 &lt;= <see cref="EmotionBehaviorMapperOptions.RestMaxAlertness"/>
///     且 心情 &lt;= <see cref="EmotionBehaviorMapperOptions.RestMaxMood"/>。</item>
///   <item><b>正常</b>：其余情况。</item>
/// </list>
/// </para>
/// </summary>
public sealed class EmotionBehaviorMapper : IEmotionBehaviorMapper
{
    private readonly EmotionBehaviorMapperOptions _opts;

    /// <summary>使用默认配置构造映射器。</summary>
    public EmotionBehaviorMapper() : this(new EmotionBehaviorMapperOptions()) { }

    /// <summary>使用指定配置构造映射器。</summary>
    /// <param name="options">阈值与模式参数配置。</param>
    public EmotionBehaviorMapper(EmotionBehaviorMapperOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _opts = options;
    }

    /// <inheritdoc/>
    public BehaviorProfile GetProfile(EmotionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        // 优先级 1：谨慎（警觉度过高 或 信心过低）
        if (state.Alertness >= _opts.CautiousAlertnessThreshold ||
            state.Confidence <= _opts.CautiousConfidenceThreshold)
        {
            return _opts.CautiousProfile;
        }

        // 优先级 2：探索（好奇心高 且 心情好）
        if (state.Curiosity >= _opts.ExploreMinCuriosity &&
            state.Mood >= _opts.ExploreMinMood)
        {
            return _opts.ExploreProfile;
        }

        // 优先级 3：休息（警觉度低 且 心情低落）
        if (state.Alertness <= _opts.RestMaxAlertness &&
            state.Mood <= _opts.RestMaxMood)
        {
            return _opts.RestProfile;
        }

        // 默认：正常
        return _opts.NormalProfile;
    }
}
