using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Agent;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Core;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 运行时实体（有行为的聚合根）。
/// <para>
/// 继承 <see cref="MicroObject"/> 以接入 MicroClaw.Core 的组件模式；
/// 实现 <see cref="IMicroAgent"/> 对外暴露执行契约。
/// 构造保持 <c>private</c>，外部只能通过 <see cref="Create"/> 工厂方法创建实例。
/// </para>
/// <para>
/// 本轮骨架阶段（P1-01）：属性委托内部 <see cref="AgentDto"/> 实体；
/// 运行时依赖（ProviderService、ToolCollector 等）在 P1-02 的 <c>OnInitializedAsync</c> 中惰性解析；
/// ReAct 执行逻辑在 P1-03 <c>StreamAsync</c> 中迁入；工具直调逻辑在 P1-04 迁入。
/// </para>
/// </summary>
public sealed class MicroAgent : MicroObject, IMicroAgent
{
    private readonly IServiceProvider _sp;

    private MicroAgent(AgentDto entity, IServiceProvider sp)
    {
        Entity = entity ?? throw new ArgumentNullException(nameof(entity));
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
    }

    // ── 内部实体 ─────────────────────────────────────────────────────────

    /// <summary>持久化实体数据（只读委托源）。</summary>
    internal AgentDto Entity { get; private set; }

    // ── IMicroAgent 属性委托 ─────────────────────────────────────────────

    /// <inheritdoc/>
    public string Id => Entity.Id;

    /// <inheritdoc/>
    public string Name => Entity.Name;

    /// <inheritdoc/>
    public bool IsEnabled => Entity.IsEnabled;

    /// <inheritdoc/>
    public bool IsDefault => Entity.IsDefault;

    // ── 工厂方法 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 根据持久化 <paramref name="entity"/> 创建运行时 MicroAgent 实例。
    /// 此阶段仅分配数据字段，IO/依赖解析推迟至 <c>OnInitializedAsync</c>（P1-02 实现）。
    /// </summary>
    public static MicroAgent Create(AgentDto entity, IServiceProvider sp) => new(entity, sp);

    // ── IMicroAgent 方法（待 P1-03 / P1-04 实现） ─────────────────────────

    /// <inheritdoc/>
    /// <remarks>P1-03 实现：迁移 AgentRunner.ExecutePreparedStreamingCoreAsync 逻辑。</remarks>
    public IAsyncEnumerable<StreamItem> StreamAsync(MicroChatContext context)
        => throw new NotImplementedException("StreamAsync will be implemented in P1-03.");

    /// <inheritdoc/>
    /// <remarks>P1-04 实现：迁移工具直调逻辑。</remarks>
    public Task<string> InvokeToolAsync(
        string toolName,
        IReadOnlyDictionary<string, string>? args,
        string fallbackInput,
        CancellationToken ct)
        => throw new NotImplementedException("InvokeToolAsync will be implemented in P1-04.");
}
