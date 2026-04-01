using Microsoft.Extensions.Configuration;

namespace MicroClaw.Configuration;

/// <summary>
/// 情绪行为模式配置，从配置文件 emotion: 节点读取。
/// 阈值决定当前情绪状态映射到哪种行为模式；各模式的推理参数影响 LLM 的 Temperature / TopP 和 System Prompt 后缀。
/// </summary>
public sealed class EmotionOptions
{
    // ── 模式切换阈值 ─────────────────────────────────────────────────────────

    /// <summary>触发「谨慎」模式的警觉度下限（含）。默认 70。</summary>
    [ConfigurationKeyName("cautious_alertness_threshold")]
    public int CautiousAlertnessThreshold { get; set; } = 70;

    /// <summary>触发「谨慎」模式的信心上限（含）。默认 30。</summary>
    [ConfigurationKeyName("cautious_confidence_threshold")]
    public int CautiousConfidenceThreshold { get; set; } = 30;

    /// <summary>触发「探索」模式的好奇心下限（含）。默认 70。</summary>
    [ConfigurationKeyName("explore_min_curiosity")]
    public int ExploreMinCuriosity { get; set; } = 70;

    /// <summary>触发「探索」模式的心情下限（含）。默认 60。</summary>
    [ConfigurationKeyName("explore_min_mood")]
    public int ExploreMinMood { get; set; } = 60;

    /// <summary>触发「休息」模式的警觉度上限（含）。默认 30。</summary>
    [ConfigurationKeyName("rest_max_alertness")]
    public int RestMaxAlertness { get; set; } = 30;

    /// <summary>触发「休息」模式的心情上限（含）。默认 40。</summary>
    [ConfigurationKeyName("rest_max_mood")]
    public int RestMaxMood { get; set; } = 40;

    // ── 正常模式推理参数 ──────────────────────────────────────────────────────

    [ConfigurationKeyName("normal_temperature")]
    public float NormalTemperature { get; set; } = 0.7f;

    [ConfigurationKeyName("normal_top_p")]
    public float NormalTopP { get; set; } = 0.9f;

    [ConfigurationKeyName("normal_system_prompt_suffix")]
    public string NormalSystemPromptSuffix { get; set; } = string.Empty;

    // ── 探索模式推理参数 ──────────────────────────────────────────────────────

    [ConfigurationKeyName("explore_temperature")]
    public float ExploreTemperature { get; set; } = 1.1f;

    [ConfigurationKeyName("explore_top_p")]
    public float ExploreTopP { get; set; } = 0.95f;

    [ConfigurationKeyName("explore_system_prompt_suffix")]
    public string ExploreSystemPromptSuffix { get; set; } = "请大胆探索，鼓励创造性思维，给出多样化的想法。";

    // ── 谨慎模式推理参数 ──────────────────────────────────────────────────────

    [ConfigurationKeyName("cautious_temperature")]
    public float CautiousTemperature { get; set; } = 0.3f;

    [ConfigurationKeyName("cautious_top_p")]
    public float CautiousTopP { get; set; } = 0.8f;

    [ConfigurationKeyName("cautious_system_prompt_suffix")]
    public string CautiousSystemPromptSuffix { get; set; } = "请谨慎行事，仔细验证每一步，不确定时优先寻求确认而非猜测。";

    // ── 休息模式推理参数 ──────────────────────────────────────────────────────

    [ConfigurationKeyName("rest_temperature")]
    public float RestTemperature { get; set; } = 0.5f;

    [ConfigurationKeyName("rest_top_p")]
    public float RestTopP { get; set; } = 0.85f;

    [ConfigurationKeyName("rest_system_prompt_suffix")]
    public string RestSystemPromptSuffix { get; set; } = "请简明扼要地作答，避免过度展开。";

    // ── 事件加减分配置（null 维度 = 不变，缺省值 = 内置硬编码默认值）─────────────

    /// <summary>消息发送成功：心情 +3，信心 +2。</summary>
    [ConfigurationKeyName("delta_message_success")]
    public EmotionDeltaOptions DeltaMessageSuccess { get; set; } = new() { Mood = +3, Confidence = +2 };

    /// <summary>消息发送失败：警觉 +8，心情 -5，信心 -5。</summary>
    [ConfigurationKeyName("delta_message_failed")]
    public EmotionDeltaOptions DeltaMessageFailed { get; set; } = new() { Alertness = +8, Mood = -5, Confidence = -5 };

    /// <summary>Tool 执行成功：好奇心 +2，信心 +3。</summary>
    [ConfigurationKeyName("delta_tool_success")]
    public EmotionDeltaOptions DeltaToolSuccess { get; set; } = new() { Curiosity = +2, Confidence = +3 };

    /// <summary>Tool 执行报错：警觉 +10，心情 -3，信心 -5。</summary>
    [ConfigurationKeyName("delta_tool_error")]
    public EmotionDeltaOptions DeltaToolError { get; set; } = new() { Alertness = +10, Mood = -3, Confidence = -5 };

    /// <summary>用户满意：心情 +10，信心 +5。</summary>
    [ConfigurationKeyName("delta_user_satisfied")]
    public EmotionDeltaOptions DeltaUserSatisfied { get; set; } = new() { Mood = +10, Confidence = +5 };

    /// <summary>用户不满意：心情 -10，信心 -5，警觉 +5。</summary>
    [ConfigurationKeyName("delta_user_dissatisfied")]
    public EmotionDeltaOptions DeltaUserDissatisfied { get; set; } = new() { Mood = -10, Confidence = -5, Alertness = +5 };

    /// <summary>任务完成：心情 +8，信心 +8，警觉 -5。</summary>
    [ConfigurationKeyName("delta_task_completed")]
    public EmotionDeltaOptions DeltaTaskCompleted { get; set; } = new() { Mood = +8, Confidence = +8, Alertness = -5 };

    /// <summary>任务失败：警觉 +10，心情 -8，信心 -8。</summary>
    [ConfigurationKeyName("delta_task_failed")]
    public EmotionDeltaOptions DeltaTaskFailed { get; set; } = new() { Alertness = +10, Mood = -8, Confidence = -8 };

    /// <summary>高严重度痛觉：警觉 +22，心情 -5，信心 -18。</summary>
    [ConfigurationKeyName("delta_pain_high")]
    public EmotionDeltaOptions DeltaPainHigh { get; set; } = new() { Alertness = +22, Mood = -5, Confidence = -18 };

    /// <summary>极高严重度痛觉：警觉 +32，心情 -10，信心 -28。</summary>
    [ConfigurationKeyName("delta_pain_critical")]
    public EmotionDeltaOptions DeltaPainCritical { get; set; } = new() { Alertness = +32, Mood = -10, Confidence = -28 };
}
