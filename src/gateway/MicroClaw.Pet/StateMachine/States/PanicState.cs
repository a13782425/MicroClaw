namespace MicroClaw.Pet.StateMachine.States;

/// <summary>Panic 状态：检测到异常，暂停自主行为，等待恢复。</summary>
public sealed class PanicState : PetStateDefinition
{
    public override PetBehaviorState Type => PetBehaviorState.Panic;
    public override string DisplayName => "Panic";
    public override string Description => "异常模式，暂停自主行为";
    public override string ApplicableScenes => "多次失败、速率已耗尽、系统异常";
    public override string? StateMachinePromptFragment => "处于 Panic 状态时，不应安排任何动作，保持等待直至系统恢复正常。";
}
