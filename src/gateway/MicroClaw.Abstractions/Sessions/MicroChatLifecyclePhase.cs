namespace MicroClaw.Abstractions;

/// <summary>
/// Pet 对话生命周期阶段枚举。
/// <para>
/// <see cref="MicroClaw.Abstractions.Pet.IPet"/> 作为会话编排器（类似行为树驱动的 NPC），
/// 一次用户消息到最终回复被切成若干阶段（Phase）。<c>PetComponent</c> 通过覆写对应的
/// <c>On…Async</c> 虚方法把行为注入到指定 Phase 上；默认 no-op，未覆写的 Phase 零开销。
/// </para>
/// <para>
/// 语义分层（由外到内，→ 表示时序，↻ 表示按派发目标重复）：
/// <code>
/// Decorate装配提示词
///                 │
///                 ↻ 对每个派发目标 Agent：
///                   BeforeDispatch
///                     │
///                     ↻ [LLM 决定调用工具时]
///                       PreToolUse ─► (成功: PostToolUse) / (失败: ToolUseFailure)
///                     │
///                   AfterDispatch
///                 │
///       异常路径：OnError / OnCanceled
///       （与正常结束的 AfterDispatch/AfterDecorate 互斥）
/// </code>
/// </para>
/// <para>
/// Pet 未启用（<c>IsEnabled == false</c>）时，当前 <see cref="MicroClaw.Abstractions.Pet.IPet"/>
/// 实现会透传 AgentRunner 流；各 Phase 是否 fire 由编排实现自行决定。
/// </para>
/// </summary>
public enum MicroChatLifecyclePhase
{
    
    /// <summary>
    /// 装配：为单个派发目标 Agent 构造请求上下文——注入 System Prompt 片段、工具、技能、RAG 文档等。
    /// <para>
    /// 每次只装配一次
    /// </para>
    /// </summary>
    Decorate,
    
    /// <summary>
    /// 派发前：单个 Agent 请求已装配完成，即将调用 AgentRunner / LLM。
    /// 每个 dispatch 精确 1 次（多 Agent 编排时 N 次）。
    /// </summary>
    BeforeDispatch,

    /// <summary>
    /// 工具调用前：LLM 决定调用某个工具，执行前的拦截/校验/改写时机。
    /// <para>
    /// 预留枚举值——当前版本 <c>MicroPet</c> 尚未把工具事件接入生命周期，后续迭代会与现有
    /// <c>IHookExecutor</c> 对齐整合。
    /// </para>
    /// </summary>
    PreToolUse,

    /// <summary>
    /// 工具调用后（成功）：工具执行完成后的观察/上报时机。
    /// <para>同 <see cref="PreToolUse"/>——预留枚举值，当前版本未接线。</para>
    /// </summary>
    PostToolUse,

    /// <summary>
    /// 工具调用失败：工具执行抛错后的补偿/降级/上报时机。
    /// <para>同 <see cref="PreToolUse"/>——预留枚举值，当前版本未接线。</para>
    /// </summary>
    ToolUseFailure,

    /// <summary>
    /// 派发后：当前 dispatch 的 LLM 往返和工具调用都已完成，assistant 最终消息已就绪。
    /// 每个 dispatch 精确 1 次（多 Agent 编排时 N 次），典型用途是持久化本次回复、回推到 Channel、
    /// 情绪更新、埋点等"出口"类副作用。
    /// </summary>
    AfterDispatch,

    /// <summary>
    /// 异常：turn 内任何阶段抛出未被组件吞掉的异常。
    /// 与正常路径的 <see cref="AfterDispatch"/> 互斥，每 turn 至多 1 次。
    /// </summary>
    OnError,

    /// <summary>
    /// 取消：turn 被 <see cref="System.Threading.CancellationToken"/> 取消。
    /// 与正常路径的 <see cref="AfterDispatch"/> 互斥，每 turn 至多 1 次。
    /// </summary>
    OnCanceled,
}
