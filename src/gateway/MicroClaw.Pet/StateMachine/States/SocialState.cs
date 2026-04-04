namespace MicroClaw.Pet.StateMachine.States;

/// <summary>社交状态：主动与用户互动，分享想法或观察。</summary>
public sealed class SocialState : PetStateDefinition
{
    public override PetBehaviorState Type => PetBehaviorState.Social;
    public override string DisplayName => "Social";
    public override string Description => "主动社交，与用户分享想法";
    public override string ApplicableScenes => "心情好、有趣的发现想分享、用户活跃";
    public override IReadOnlyList<PetActionType> AllowedActions => [PetActionType.NotifyUser, PetActionType.DelegateToAgent];
    public override string? PersonalityContextHint => "当前处于社交状态，倾向于主动分享有趣的发现和想法，语气更加活跃热情。";
}
