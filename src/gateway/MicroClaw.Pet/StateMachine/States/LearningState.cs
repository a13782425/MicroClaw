namespace MicroClaw.Pet.StateMachine.States;

/// <summary>学习状态：正在从会话内容或外部资源中获取知识。</summary>
public sealed class LearningState : PetStateDefinition
{
    public override PetBehaviorState Type => PetBehaviorState.Learning;
    public override string DisplayName => "Learning";
    public override string Description => "学习中，从内容或外部资源中获取知识";
    public override string ApplicableScenes => "发现值得学习的内容、好奇心高、有新领域";
    public override IReadOnlyList<PetActionType> AllowedActions => [PetActionType.FetchWeb, PetActionType.SummarizeToMemory];
}
