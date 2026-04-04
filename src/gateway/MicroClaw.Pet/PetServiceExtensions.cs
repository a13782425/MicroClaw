using Microsoft.Extensions.DependencyInjection;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.StateMachine.States;

namespace MicroClaw.Pet;

/// <summary>
/// Pet 状态机 DI 注册扩展方法。
/// </summary>
public static class PetServiceExtensions
{
    /// <summary>
    /// 注册所有内置 Pet 状态定义、<see cref="PetStateRegistry"/> 和 <see cref="PetStateMachinePrompt"/>。
    /// </summary>
    public static IServiceCollection AddPetStates(this IServiceCollection services)
    {
        services.AddSingleton<IPetStateDefinition, IdleState>();
        services.AddSingleton<IPetStateDefinition, LearningState>();
        services.AddSingleton<IPetStateDefinition, OrganizingState>();
        services.AddSingleton<IPetStateDefinition, RestingState>();
        services.AddSingleton<IPetStateDefinition, ReflectingState>();
        services.AddSingleton<IPetStateDefinition, SocialState>();
        services.AddSingleton<IPetStateDefinition, PanicState>();
        services.AddSingleton<IPetStateDefinition, DispatchingState>();
        services.AddSingleton<PetStateRegistry>();
        services.AddSingleton<PetStateMachinePrompt>();
        return services;
    }

    /// <summary>
    /// 注册自定义 Pet 状态定义。必须在 <see cref="AddPetStates"/> 之前调用。
    /// </summary>
    public static IServiceCollection AddPetState<TState>(this IServiceCollection services)
        where TState : class, IPetStateDefinition
    {
        services.AddSingleton<IPetStateDefinition, TState>();
        return services;
    }
}
