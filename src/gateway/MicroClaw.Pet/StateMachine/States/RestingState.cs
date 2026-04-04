namespace MicroClaw.Pet.StateMachine.States;

/// <summary>休息状态：低活跃，减少 LLM 调用，仅响应必要消息。</summary>
public sealed class RestingState : PetStateDefinition
{
    public override PetBehaviorState Type => PetBehaviorState.Resting;
    public override string DisplayName => "Resting";
    public override string Description => "休息，减少 LLM 调用";
    public override string ApplicableScenes => "速率配额紧张、疲劳度高（警觉度低）、非活跃时间";
    public override string? StateMachinePromptFragment => "处于 Resting 状态时，避免安排高消耗动作，优先让 Pet 放松恢复。";
}
