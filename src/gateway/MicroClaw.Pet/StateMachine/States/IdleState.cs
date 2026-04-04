namespace MicroClaw.Pet.StateMachine.States;

/// <summary>空闲状态：等待消息，无自主活动。</summary>
public sealed class IdleState : PetStateDefinition
{
    public override PetBehaviorState Type => PetBehaviorState.Idle;
    public override string DisplayName => "Idle";
    public override string Description => "空闲，等待消息";
    public override string ApplicableScenes => "无待处理任务，用户不活跃";
}
