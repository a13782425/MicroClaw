
namespace MicroClaw.Configuration;

/// <summary>
/// 情绪行为模式配置，从配置文件 emotion: 节点读取。
/// 阈值决定当前情绪状态映射到哪种行为模式；各模式的推理参数影响 LLM 的 Temperature / TopP 和 System Prompt 后缀。
/// </summary>
[MicroClawYamlConfig("emotion", FileName = "emotion.yaml", IsWritable = true)]
public sealed class EmotionOptions : IMicroClawConfigTemplate
{
    // ── 模式切换阈值 ─────────────────────────────────────────────────────────

    /// <summary>触发「谨慎」模式的警觉度下限（含）。默认 70。</summary>
    [YamlMember(Alias = "cautious_alertness_threshold", Description = "触发谨慎模式的警觉度下限。")]
    public int CautiousAlertnessThreshold { get; set; } = 70;

    /// <summary>触发「谨慎」模式的信心上限（含）。默认 30。</summary>
    [YamlMember(Alias = "cautious_confidence_threshold", Description = "触发谨慎模式的信心上限。")]
    public int CautiousConfidenceThreshold { get; set; } = 30;

    /// <summary>触发「探索」模式的好奇心下限（含）。默认 70。</summary>
    [YamlMember(Alias = "explore_min_curiosity", Description = "触发探索模式的好奇心下限。")]
    public int ExploreMinCuriosity { get; set; } = 70;

    /// <summary>触发「探索」模式的心情下限（含）。默认 60。</summary>
    [YamlMember(Alias = "explore_min_mood", Description = "触发探索模式的心情下限。")]
    public int ExploreMinMood { get; set; } = 60;

    /// <summary>触发「休息」模式的警觉度上限（含）。默认 30。</summary>
    [YamlMember(Alias = "rest_max_alertness", Description = "触发休息模式的警觉度上限。")]
    public int RestMaxAlertness { get; set; } = 30;

    /// <summary>触发「休息」模式的心情上限（含）。默认 40。</summary>
    [YamlMember(Alias = "rest_max_mood", Description = "触发休息模式的心情上限。")]
    public int RestMaxMood { get; set; } = 40;

    // ── 正常模式推理参数 ──────────────────────────────────────────────────────

    /// <summary>
    /// 正常模式下的 Temperature 参数。
    /// </summary>
    [YamlMember(Alias = "normal_temperature", Description = "正常模式下的 Temperature 参数。")]
    public float NormalTemperature { get; set; } = 0.7f;

    /// <summary>
    /// 正常模式下的 TopP 参数。
    /// </summary>
    [YamlMember(Alias = "normal_top_p", Description = "正常模式下的 TopP 参数。")]
    public float NormalTopP { get; set; } = 0.9f;

    /// <summary>
    /// 正常模式附加到系统提示词末尾的补充文本。
    /// </summary>
    [YamlMember(Alias = "normal_system_prompt_suffix", Description = "正常模式附加到系统提示词末尾的补充文本。")]
    public string NormalSystemPromptSuffix { get; set; } = string.Empty;

    // ── 探索模式推理参数 ──────────────────────────────────────────────────────

    /// <summary>
    /// 探索模式下的 Temperature 参数。
    /// </summary>
    [YamlMember(Alias = "explore_temperature", Description = "探索模式下的 Temperature 参数。")]
    public float ExploreTemperature { get; set; } = 1.1f;

    /// <summary>
    /// 探索模式下的 TopP 参数。
    /// </summary>
    [YamlMember(Alias = "explore_top_p", Description = "探索模式下的 TopP 参数。")]
    public float ExploreTopP { get; set; } = 0.95f;

    /// <summary>
    /// 探索模式附加到系统提示词末尾的补充文本。
    /// </summary>
    [YamlMember(Alias = "explore_system_prompt_suffix", Description = "探索模式附加到系统提示词末尾的补充文本。")]
    public string ExploreSystemPromptSuffix { get; set; } = "请大胆探索，鼓励创造性思维，给出多样化的想法。";

    // ── 谨慎模式推理参数 ──────────────────────────────────────────────────────

    /// <summary>
    /// 谨慎模式下的 Temperature 参数。
    /// </summary>
    [YamlMember(Alias = "cautious_temperature", Description = "谨慎模式下的 Temperature 参数。")]
    public float CautiousTemperature { get; set; } = 0.3f;

    /// <summary>
    /// 谨慎模式下的 TopP 参数。
    /// </summary>
    [YamlMember(Alias = "cautious_top_p", Description = "谨慎模式下的 TopP 参数。")]
    public float CautiousTopP { get; set; } = 0.8f;

    /// <summary>
    /// 谨慎模式附加到系统提示词末尾的补充文本。
    /// </summary>
    [YamlMember(Alias = "cautious_system_prompt_suffix", Description = "谨慎模式附加到系统提示词末尾的补充文本。")]
    public string CautiousSystemPromptSuffix { get; set; } = "请谨慎行事，仔细验证每一步，不确定时优先寻求确认而非猜测。";

    // ── 休息模式推理参数 ──────────────────────────────────────────────────────

    /// <summary>
    /// 休息模式下的 Temperature 参数。
    /// </summary>
    [YamlMember(Alias = "rest_temperature", Description = "休息模式下的 Temperature 参数。")]
    public float RestTemperature { get; set; } = 0.5f;

    /// <summary>
    /// 休息模式下的 TopP 参数。
    /// </summary>
    [YamlMember(Alias = "rest_top_p", Description = "休息模式下的 TopP 参数。")]
    public float RestTopP { get; set; } = 0.85f;

    /// <summary>
    /// 休息模式附加到系统提示词末尾的补充文本。
    /// </summary>
    [YamlMember(Alias = "rest_system_prompt_suffix", Description = "休息模式附加到系统提示词末尾的补充文本。")]
    public string RestSystemPromptSuffix { get; set; } = "请简明扼要地作答，避免过度展开。";

    // ── 事件加减分配置（null 维度 = 不变，缺省值 = 内置硬编码默认值）─────────────

    /// <summary>消息发送成功：心情 +3，信心 +2。</summary>
    [YamlMember(Alias = "delta_message_success", Description = "消息发送成功时的情绪增量配置。")]
    public EmotionDeltaOptions DeltaMessageSuccess { get; set; } = new() { Mood = +3, Confidence = +2 };

    /// <summary>消息发送失败：警觉 +8，心情 -5，信心 -5。</summary>
    [YamlMember(Alias = "delta_message_failed", Description = "消息发送失败时的情绪增量配置。")]
    public EmotionDeltaOptions DeltaMessageFailed { get; set; } = new() { Alertness = +8, Mood = -5, Confidence = -5 };

    /// <summary>Tool 执行成功：好奇心 +2，信心 +3。</summary>
    [YamlMember(Alias = "delta_tool_success", Description = "Tool 执行成功时的情绪增量配置。")]
    public EmotionDeltaOptions DeltaToolSuccess { get; set; } = new() { Curiosity = +2, Confidence = +3 };

    /// <summary>Tool 执行报错：警觉 +10，心情 -3，信心 -5。</summary>
    [YamlMember(Alias = "delta_tool_error", Description = "Tool 执行报错时的情绪增量配置。")]
    public EmotionDeltaOptions DeltaToolError { get; set; } = new() { Alertness = +10, Mood = -3, Confidence = -5 };

    /// <summary>用户满意：心情 +10，信心 +5。</summary>
    [YamlMember(Alias = "delta_user_satisfied", Description = "用户满意时的情绪增量配置。")]
    public EmotionDeltaOptions DeltaUserSatisfied { get; set; } = new() { Mood = +10, Confidence = +5 };

    /// <summary>用户不满意：心情 -10，信心 -5，警觉 +5。</summary>
    [YamlMember(Alias = "delta_user_dissatisfied", Description = "用户不满意时的情绪增量配置。")]
    public EmotionDeltaOptions DeltaUserDissatisfied { get; set; } = new() { Mood = -10, Confidence = -5, Alertness = +5 };

    /// <summary>任务完成：心情 +8，信心 +8，警觉 -5。</summary>
    [YamlMember(Alias = "delta_task_completed", Description = "任务完成时的情绪增量配置。")]
    public EmotionDeltaOptions DeltaTaskCompleted { get; set; } = new() { Mood = +8, Confidence = +8, Alertness = -5 };

    /// <summary>任务失败：警觉 +10，心情 -8，信心 -8。</summary>
    [YamlMember(Alias = "delta_task_failed", Description = "任务失败时的情绪增量配置。")]
    public EmotionDeltaOptions DeltaTaskFailed { get; set; } = new() { Alertness = +10, Mood = -8, Confidence = -8 };

    /// <summary>高严重度痛觉：警觉 +22，心情 -5，信心 -18。</summary>
    [YamlMember(Alias = "delta_pain_high", Description = "高严重度痛觉事件的情绪增量配置。")]
    public EmotionDeltaOptions DeltaPainHigh { get; set; } = new() { Alertness = +22, Mood = -5, Confidence = -18 };

    /// <summary>极高严重度痛觉：警觉 +32，心情 -10，信心 -28。</summary>
    [YamlMember(Alias = "delta_pain_critical", Description = "极高严重度痛觉事件的情绪增量配置。")]
    public EmotionDeltaOptions DeltaPainCritical { get; set; } = new() { Alertness = +32, Mood = -10, Confidence = -28 };

    public IMicroClawConfigOptions CreateDefaultTemplate() => new EmotionOptions();
}
