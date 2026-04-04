namespace MicroClaw.Pet.StateMachine.States;

/// <summary>反思状态：深度回顾会话历史，生成洞察。</summary>
public sealed class ReflectingState : PetStateDefinition
{
    public override PetBehaviorState Type => PetBehaviorState.Reflecting;
    public override string DisplayName => "Reflecting";
    public override string Description => "深度反思，回顾会话生成洞察";
    public override string ApplicableScenes => "会话积累足够历史、需要总结规律";
    public override IReadOnlyList<PetActionType> AllowedActions => [PetActionType.ReflectOnSession, PetActionType.EvolvePrompts];
}
