namespace MicroClaw.Pet.StateMachine.States;

/// <summary>
/// 定义一个 Pet 行为状态的特征与约束。
/// 每个实现类代表一个具体状态（如 Idle、Learning 等），作为应用级单例注册到 DI 容器，
/// 并由 <see cref="PetStateRegistry"/> 统一管理。
/// </summary>
public interface IPetStateDefinition
{
    /// <summary>行为状态枚举标识，与 <see cref="PetBehaviorState"/> 对应。</summary>
    PetBehaviorState Type { get; }

    /// <summary>状态显示名称（英文，与枚举名称一致）。</summary>
    string DisplayName { get; }

    /// <summary>状态含义简述，用于 LLM System Prompt 中的状态表。</summary>
    string Description { get; }

    /// <summary>适用场景描述，用于 LLM System Prompt 中的状态表。</summary>
    string ApplicableScenes { get; }

    /// <summary>
    /// 该状态下 LLM 可规划的动作白名单（软提示）。
    /// 注入到 User Prompt 中告知 LLM 当前状态允许的动作范围；解析阶段不强制过滤。
    /// </summary>
    IReadOnlyList<PetActionType> AllowedActions { get; }

    /// <summary>
    /// 附加给状态机 System Prompt 的额外描述片段（可为 null）。
    /// 用于描述该状态的特殊决策规则或注意事项。
    /// </summary>
    string? StateMachinePromptFragment { get; }

    /// <summary>
    /// Pet 在该状态下与用户对话时使用的个性化 Context Hint（可为 null）。
    /// 供 MicroPet 在未来版本中用于调整对话风格。
    /// </summary>
    string? PersonalityContextHint { get; }
}
