namespace MicroClaw.Pet.StateMachine.States;

/// <summary>
/// <see cref="IPetStateDefinition"/> 的抽象基类，提供无操作的默认实现。
/// 继承此类可避免为每个状态重复实现不需要的可选成员。
/// </summary>
public abstract class PetStateDefinition : IPetStateDefinition
{
    /// <inheritdoc />
    public abstract PetBehaviorState Type { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public abstract string Description { get; }

    /// <inheritdoc />
    public abstract string ApplicableScenes { get; }

    /// <inheritdoc />
    public virtual IReadOnlyList<PetActionType> AllowedActions => [];

    /// <inheritdoc />
    public virtual string? StateMachinePromptFragment => null;

    /// <inheritdoc />
    public virtual string? PersonalityContextHint => null;
}
