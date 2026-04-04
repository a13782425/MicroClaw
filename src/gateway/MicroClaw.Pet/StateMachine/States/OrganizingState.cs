namespace MicroClaw.Pet.StateMachine.States;

/// <summary>整理状态：整理记忆、归纳知识、清理冗余内容。</summary>
public sealed class OrganizingState : PetStateDefinition
{
    public override PetBehaviorState Type => PetBehaviorState.Organizing;
    public override string DisplayName => "Organizing";
    public override string Description => "整理记忆和知识";
    public override string ApplicableScenes => "RAG 知识库较大需要归类、长期未整理";
    public override IReadOnlyList<PetActionType> AllowedActions => [PetActionType.OrganizeMemory];
}
