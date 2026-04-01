using MicroClaw.Configuration;

namespace MicroClaw.Emotion;

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

    /// <summary>
    /// 从 <see cref="EmotionOptions"/> 构建由配置驱动的 <see cref="EmotionRuleEngineOptions"/>。
    /// <para>关闭内置规则，完全由 YAML 配置的 10 条事件规则驱动。</para>
    /// </summary>
    public static EmotionRuleEngineOptions FromEmotionOptions(EmotionOptions opts)
    {
        static EmotionDelta ToDelta(EmotionDeltaOptions d) =>
            new(Alertness:  d.Alertness  ?? 0,
                Mood:       d.Mood       ?? 0,
                Curiosity:  d.Curiosity  ?? 0,
                Confidence: d.Confidence ?? 0);

        return new EmotionRuleEngineOptions
        {
            UseDefaultRules = false,
            CustomRules =
            [
                new(EmotionEventType.MessageSuccess,    ToDelta(opts.DeltaMessageSuccess),    "消息发送成功"),
                new(EmotionEventType.MessageFailed,     ToDelta(opts.DeltaMessageFailed),     "消息发送失败"),
                new(EmotionEventType.ToolSuccess,       ToDelta(opts.DeltaToolSuccess),       "Tool 执行成功"),
                new(EmotionEventType.ToolError,         ToDelta(opts.DeltaToolError),         "Tool 执行报错"),
                new(EmotionEventType.UserSatisfied,     ToDelta(opts.DeltaUserSatisfied),     "用户满意"),
                new(EmotionEventType.UserDissatisfied,  ToDelta(opts.DeltaUserDissatisfied),  "用户不满意"),
                new(EmotionEventType.TaskCompleted,     ToDelta(opts.DeltaTaskCompleted),     "任务完成"),
                new(EmotionEventType.TaskFailed,        ToDelta(opts.DeltaTaskFailed),        "任务失败"),
                new(EmotionEventType.PainOccurredHigh,    ToDelta(opts.DeltaPainHigh),    "高严重度痛觉"),
                new(EmotionEventType.PainOccurredCritical, ToDelta(opts.DeltaPainCritical), "极高严重度痛觉"),
            ],
        };
    }
}

