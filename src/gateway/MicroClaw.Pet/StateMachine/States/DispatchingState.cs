namespace MicroClaw.Pet.StateMachine.States;

/// <summary>Dispatching 状态：正在调度用户消息，由系统设置，心跳不应主动切换至此。</summary>
public sealed class DispatchingState : PetStateDefinition
{
    public override PetBehaviorState Type => PetBehaviorState.Dispatching;
    public override string DisplayName => "Dispatching";
    public override string Description => "正在调度用户消息";
    public override string ApplicableScenes => "仅在处理用户消息时由系统设置，心跳不应主动切换到此状态";
    public override IReadOnlyList<PetActionType> AllowedActions => [PetActionType.DelegateToAgent];
    public override string? StateMachinePromptFragment => "心跳中不应主动进入 Dispatching 状态，该状态仅在处理用户消息时使用。";
}
