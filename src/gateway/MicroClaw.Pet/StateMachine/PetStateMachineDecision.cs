using MicroClaw.Pet.Emotion;

namespace MicroClaw.Pet.StateMachine;

/// <summary>
/// PetStateMachine LLM 调用的结构化输出。
/// 描述 LLM 对 Pet 当前状态的决策：新状态、情绪变化、原因、计划动作。
/// </summary>
public sealed record PetStateMachineDecision
{
    /// <summary>LLM 决定的新行为状态。</summary>
    public PetBehaviorState NewState { get; init; }

    /// <summary>情绪变化量（四维增减）。</summary>
    public EmotionDelta EmotionShift { get; init; } = EmotionDelta.Zero;

    /// <summary>决策原因说明。</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>LLM 决定执行的计划动作列表（可能为空）。</summary>
    public IReadOnlyList<PetPlannedAction> PlannedActions { get; init; } = [];
}
