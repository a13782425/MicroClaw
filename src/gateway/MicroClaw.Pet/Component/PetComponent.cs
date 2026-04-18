using MicroClaw.Abstractions;

namespace MicroClaw.Pet;

/// <summary>
/// Pet 组件的抽象基类：继承自 <see cref="MicroClaw.Core.MicroComponent"/>，挂在 <see cref="MicroPet"/>
/// （本身是 <see cref="MicroClaw.Core.MicroObject"/>）上。
/// <para>
/// 子类通过覆写 <c>On…Async</c> 系列虚方法把行为注入到 <see cref="ChatLifecyclePhase"/> 对应阶段；
/// 默认实现全为 no-op，未覆写的 Phase 零开销。
/// </para>
/// <para>
/// 同一 Phase 内多个组件按 <see cref="Order"/> 升序串行回调；相同 <see cref="Order"/> 的执行顺序未定义，
/// 组件应避免对彼此状态产生写冲突。
/// </para>
/// <para>
/// 阶段与虚方法对应关系（详细语义见 <see cref="ChatLifecyclePhase"/>）：
/// <list type="bullet">
///   <item><description><see cref="ChatLifecyclePhase.BeforeDecorate"/> → <see cref="OnBeforeDecorateAsync"/></description></item>
///   <item><description><see cref="ChatLifecyclePhase.Decorate"/> → <see cref="OnDecorateAsync"/></description></item>
///   <item><description><see cref="ChatLifecyclePhase.AfterDecorate"/> → <see cref="OnAfterDecorateAsync"/></description></item>
///   <item><description><see cref="ChatLifecyclePhase.BeforeDispatch"/> → <see cref="OnBeforeDispatchAsync"/></description></item>
///   <item><description><see cref="ChatLifecyclePhase.PreToolUse"/> → <see cref="OnPreToolUseAsync"/>（预留未接线）</description></item>
///   <item><description><see cref="ChatLifecyclePhase.PostToolUse"/> → <see cref="OnPostToolUseAsync"/>（预留未接线）</description></item>
///   <item><description><see cref="ChatLifecyclePhase.ToolUseFailure"/> → <see cref="OnToolUseFailureAsync"/>（预留未接线）</description></item>
///   <item><description><see cref="ChatLifecyclePhase.AfterDispatch"/> → <see cref="OnAfterDispatchAsync"/></description></item>
///   <item><description><see cref="ChatLifecyclePhase.OnError"/> → <see cref="OnErrorAsync"/>（预留未接线）</description></item>
///   <item><description><see cref="ChatLifecyclePhase.OnCanceled"/> → <see cref="OnCanceledAsync"/>（预留未接线）</description></item>
/// </list>
/// </para>
/// </summary>
/// <remarks>
/// <para>典型子类用法（尚未在本轮落地，仅做参考示例）：</para>
/// <code>
/// /// &lt;summary&gt;
/// /// 在 AfterDispatch 阶段将 assistant 最终消息回推到 Channel。
/// /// &lt;/summary&gt;
/// public sealed class SessionForwardComponent : PetComponent
/// {
///     protected internal override int Order =&gt; 100;
///
///     protected internal override async ValueTask OnAfterDispatchAsync(ChatContext ctx)
///     {
///         if (ctx.FinalAssistantMessage is { } reply &amp;&amp; ctx.Session.Channel is { } channel)
///             await channel.HandleSessionMessageAsync(/* ... */, ctx.Ct);
///     }
/// }
/// </code>
/// </remarks>
public abstract class PetComponent : MicroClaw.Core.MicroComponent
{
    /// <summary>
    /// 同一 <see cref="ChatLifecyclePhase"/> 内的排序权值，越小越先执行，默认 0。
    /// </summary>
    protected internal virtual int Order => 0;

    // ── Decorate 层（BeforeDecorate/AfterDecorate turn 级；Decorate per-dispatch） ──
    
    /// <summary>
    /// <see cref="ChatLifecyclePhase.Decorate"/> 回调：为单个 Agent 请求装配/装饰上下文——
    /// 注入 System Prompt 片段、工具、技能、RAG 文档等。多 Agent 编排时对每个 Agent 各 1 次；
    /// 多个组件按 <see cref="Order"/> 升序合并贡献。默认 no-op。
    /// </summary>
    protected internal virtual ValueTask OnDecorateAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    // ── 派发层（每个 dispatch 1 次） ───────────────────────────────────────────

    /// <summary>
    /// <see cref="ChatLifecyclePhase.BeforeDispatch"/> 回调：Agent 请求已装配完成，即将调用
    /// AgentRunner / LLM。每个 dispatch 精确 1 次。默认 no-op。
    /// </summary>
    protected internal virtual ValueTask OnBeforeDispatchAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    /// <summary>
    /// <see cref="ChatLifecyclePhase.AfterDispatch"/> 回调：当前 dispatch 的 LLM 往返和工具调用都已完成，
    /// assistant 最终消息已就绪。典型用途：持久化、回推到 Channel、情绪更新、埋点等。默认 no-op。
    /// </summary>
    protected internal virtual ValueTask OnAfterDispatchAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    // ── 工具层（每次 tool call 1 次，本版本未接线） ─────────────────────────────

    /// <summary>
    /// <see cref="ChatLifecyclePhase.PreToolUse"/> 回调：LLM 决定调用工具前的拦截/校验/改写时机。
    /// <para>预留——本版本 <see cref="MicroPet"/> 尚未把工具事件接入生命周期，此回调不会被触发。</para>
    /// </summary>
    protected internal virtual ValueTask OnPreToolUseAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    /// <summary>
    /// <see cref="ChatLifecyclePhase.PostToolUse"/> 回调：工具成功执行完成后的观察/上报时机。
    /// <para>预留——同上，本版本未接线。</para>
    /// </summary>
    protected internal virtual ValueTask OnPostToolUseAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    /// <summary>
    /// <see cref="ChatLifecyclePhase.ToolUseFailure"/> 回调：工具执行抛错后的补偿/降级/上报时机。
    /// <para>预留——同上，本版本未接线。</para>
    /// </summary>
    protected internal virtual ValueTask OnToolUseFailureAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    // ── 异常路径（每 turn 至多 1 次，本版本未接线） ─────────────────────────────

    /// <summary>
    /// <see cref="ChatLifecyclePhase.OnError"/> 回调：turn 内抛出未被组件吞掉的异常。
    /// <para>预留——本版本 <see cref="MicroPet"/> 尚未派发异常阶段。</para>
    /// </summary>
    protected internal virtual ValueTask OnErrorAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    /// <summary>
    /// <see cref="ChatLifecyclePhase.OnCanceled"/> 回调：turn 被取消。
    /// <para>预留——本版本 <see cref="MicroPet"/> 尚未派发取消阶段。</para>
    /// </summary>
    protected internal virtual ValueTask OnCanceledAsync(MicroChatContext ctx) => ValueTask.CompletedTask;
}
