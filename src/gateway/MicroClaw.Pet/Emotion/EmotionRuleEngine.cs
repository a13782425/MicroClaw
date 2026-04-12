namespace MicroClaw.Pet.Emotion;

/// <summary>
/// 基于规则列表的情绪触发引擎实现。
/// <para>
/// 内置默认规则覆盖常见场景（消息成功/失败、Tool 报错、用户满意度、任务完成/失败）。
/// 可通过 <see cref="EmotionRuleEngineOptions"/> 追加或完全替换规则。
/// </para>
/// <para>
/// 同一事件类型的多条规则按顺序 <see cref="EmotionDelta.Merge"/> 叠加后一次性施加。
/// </para>
/// </summary>
public sealed class EmotionRuleEngine : IEmotionRuleEngine
{
    /// <summary>内置默认规则集。</summary>
    public static IReadOnlyList<EmotionRule> DefaultRules { get; } =
    [
        // ── 消息层 ──
        new(EmotionEventType.MessageSuccess,
            new EmotionDelta(Mood: +3, Confidence: +2),
            "消息顺利发出，心情和信心小幅提升"),

        new(EmotionEventType.MessageFailed,
            new EmotionDelta(Alertness: +8, Mood: -5, Confidence: -5),
            "消息失败，进入警觉状态并降低信心"),

        // ── Tool 层 ──
        new(EmotionEventType.ToolSuccess,
            new EmotionDelta(Curiosity: +2, Confidence: +3),
            "工具执行成功，好奇心与信心上升"),

        new(EmotionEventType.ToolError,
            new EmotionDelta(Alertness: +10, Mood: -3, Confidence: -5),
            "工具报错，高度警觉，信心下降"),

        // ── 用户反馈层 ──
        new(EmotionEventType.UserSatisfied,
            new EmotionDelta(Mood: +10, Confidence: +5),
            "用户满意，心情大幅提升"),

        new(EmotionEventType.UserDissatisfied,
            new EmotionDelta(Mood: -10, Confidence: -5, Alertness: +5),
            "用户不满，心情下降，进入谨慎状态"),

        // ── 任务层 ──
        new(EmotionEventType.TaskCompleted,
            new EmotionDelta(Mood: +8, Confidence: +8, Alertness: -5),
            "任务完成，放松且自信"),

        new(EmotionEventType.TaskFailed,
            new EmotionDelta(Alertness: +10, Mood: -8, Confidence: -8),
            "任务失败，高度警觉，心情与信心双降"),
    ];

    private readonly IReadOnlyList<EmotionRule> _rules;

    /// <summary>
    /// 使用默认配置构造规则引擎（启用所有内置规则，无自定义规则）。
    /// </summary>
    public EmotionRuleEngine() : this(new EmotionRuleEngineOptions()) { }

    /// <summary>
    /// 使用指定配置构造规则引擎。
    /// </summary>
    /// <param name="options">配置选项。</param>
    public EmotionRuleEngine(EmotionRuleEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var rules = new List<EmotionRule>();
        if (options.UseDefaultRules)
            rules.AddRange(DefaultRules);
        rules.AddRange(options.CustomRules);

        _rules = rules;
    }

    /// <inheritdoc/>
    public EmotionDelta GetDelta(EmotionEventType eventType)
    {
        var delta = EmotionDelta.Zero;
        foreach (var rule in _rules)
        {
            if (rule.EventType == eventType)
                delta = delta.Merge(rule.Delta);
        }
        return delta;
    }

    /// <inheritdoc/>
    public EmotionState Evaluate(EmotionState current, EmotionEventType eventType)
    {
        ArgumentNullException.ThrowIfNull(current);
        return current.Apply(GetDelta(eventType));
    }
}
