namespace MicroClaw.Pet.Emotion;

/// <summary>
/// 行为模式的推理参数配置，由 <see cref="IEmotionBehaviorMapper"/> 根据情绪状态生成。
/// 所有属性均为不可变。
/// </summary>
/// <param name="Mode">当前行为模式。</param>
/// <param name="Temperature">LLM 采样温度，值域 [0.0, 2.0]。</param>
/// <param name="TopP">LLM 核采样概率，值域 (0.0, 1.0]。</param>
/// <param name="SystemPromptSuffix">
/// 追加到系统提示末尾的提示语（可为空字符串），用于传导行为模式语义。
/// </param>
public sealed record BehaviorProfile(
    BehaviorMode Mode,
    float Temperature,
    float TopP,
    string SystemPromptSuffix)
{
    /// <summary>正常模式的默认推理参数。</summary>
    public static readonly BehaviorProfile DefaultNormal = new(
        BehaviorMode.Normal,
        Temperature: 0.7f,
        TopP: 0.9f,
        SystemPromptSuffix: string.Empty);

    /// <summary>探索模式的默认推理参数。</summary>
    public static readonly BehaviorProfile DefaultExplore = new(
        BehaviorMode.Explore,
        Temperature: 1.1f,
        TopP: 0.95f,
        SystemPromptSuffix: "请大胆探索，鼓励创造性思维，给出多样化的想法。");

    /// <summary>谨慎模式的默认推理参数。</summary>
    public static readonly BehaviorProfile DefaultCautious = new(
        BehaviorMode.Cautious,
        Temperature: 0.3f,
        TopP: 0.8f,
        SystemPromptSuffix: "请谨慎行事，仔细验证每一步，不确定时优先寻求确认而非猜测。");

    /// <summary>休息模式的默认推理参数。</summary>
    public static readonly BehaviorProfile DefaultRest = new(
        BehaviorMode.Rest,
        Temperature: 0.5f,
        TopP: 0.85f,
        SystemPromptSuffix: "请简明扼要地作答，避免过度展开。");
}
