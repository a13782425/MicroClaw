namespace MicroClaw.Pet.StateMachine.States;

/// <summary>
/// Pet 状态定义注册表。
/// 聚合所有已注册的 <see cref="IPetStateDefinition"/>，按 <see cref="PetBehaviorState"/> 枚举类型索引，
/// 支持运行时查询状态定义。注册为应用级单例。
/// </summary>
public sealed class PetStateRegistry
{
    private readonly Dictionary<PetBehaviorState, IPetStateDefinition> _definitions;

    public PetStateRegistry(IEnumerable<IPetStateDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = definitions.ToDictionary(d => d.Type);
    }

    /// <summary>所有已注册的状态定义。</summary>
    public IReadOnlyCollection<IPetStateDefinition> All => _definitions.Values;

    /// <summary>
    /// 获取指定状态的定义。若未注册则抛出 <see cref="KeyNotFoundException"/>。
    /// </summary>
    public IPetStateDefinition Get(PetBehaviorState state) =>
        _definitions.TryGetValue(state, out var def)
            ? def
            : throw new KeyNotFoundException($"未找到 Pet 状态定义: {state}");

    /// <summary>
    /// 尝试获取指定状态的定义。
    /// </summary>
    public bool TryGet(PetBehaviorState state, out IPetStateDefinition? definition) =>
        _definitions.TryGetValue(state, out definition);
}
