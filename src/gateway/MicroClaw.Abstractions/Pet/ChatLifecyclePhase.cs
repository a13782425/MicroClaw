namespace MicroClaw.Abstractions.Pet;

/// <summary>
/// Pet 对话生命周期的阶段枚举。
/// <para>
/// 一次用户消息到回复的生命周期被拆成四个阶段（Phase），Pet 组件（<c>PetComponent</c>）
/// 通过 <see cref="IChatLifecycleStep"/> 把自己的行为注册到具体 Phase 上。
/// </para>
/// <para>
/// 顺序：<see cref="BeforeChat"/> → （<see cref="BeforeToolCall"/> / <see cref="AfterToolCall"/> 可能在
/// 对话进行中反复发生 0..N 次） → <see cref="AfterChat"/>。
/// </para>
/// </summary>
public enum ChatLifecyclePhase
{
    /// <summary>
    /// 对话前：构造 System Prompt 片段、做决策、检查限流、装填 RAG 等在发起 LLM 调用之前完成的工作。
    /// </summary>
    BeforeChat,

    /// <summary>
    /// 工具调用前：LLM 决定调用某个工具，执行前的拦截/校验/改写时机。
    /// <para>
    /// 预留枚举值——本版本 <c>MicroPet</c> 编排器尚未把事件源接到 <see cref="BeforeChat"/>/<see cref="AfterChat"/> 之外，
    /// 工具钩子的注入留待后续迭代与现有 <c>IHookExecutor</c> 一起整合。
    /// </para>
    /// </summary>
    BeforeToolCall,

    /// <summary>
    /// 工具调用后：工具执行完成（成功或失败）后的观察/上报时机。
    /// <para>同上——预留枚举值，本版本未接线。</para>
    /// </summary>
    AfterToolCall,

    /// <summary>
    /// 对话结束：所有 LLM 往返已经结束，assistant 最终消息已就绪，组件在这里做持久化、转发到 Channel、
    /// 情绪更新、埋点等"出口"类副作用。
    /// </summary>
    AfterChat,
}
