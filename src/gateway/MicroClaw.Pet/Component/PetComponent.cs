using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;

namespace MicroClaw.Pet;

/// <summary>
/// Pet 组件的抽象基类：继承自 <see cref="MicroClaw.Core.MicroComponent"/>，挂在 <see cref="MicroPet"/>
/// （本身是 <see cref="MicroClaw.Core.MicroObject"/>）上。
/// <para>
/// 子类通过覆写 <c>On…Async</c> 系列虚方法把行为注入到 <see cref="MicroChatLifecyclePhase"/> 对应阶段；
/// 默认实现全为 no-op，未覆写的 Phase 零开销。
/// </para>
/// <para>
/// 同一 Phase 内多个组件按 <see cref="Order"/> 升序串行回调；相同 <see cref="Order"/> 的执行顺序未定义，
/// 组件应避免对彼此状态产生写冲突。
/// </para>
/// <para>
/// 阶段与虚方法对应关系（详细语义见 <see cref="MicroChatLifecyclePhase"/>）：
/// <list type="bullet">
///   <item><description><see cref="MicroChatLifecyclePhase.Decorate"/> → <see cref="OnDecorateAsync"/>（当前实际 Agent 调用路径已触发）</description></item>
///   <item><description><see cref="MicroChatLifecyclePhase.BeforeDispatch"/> → <see cref="OnBeforeDispatchAsync"/>（当前实际 Agent 调用路径已触发，且位于 Decorate 之后）</description></item>
///   <item><description><see cref="MicroChatLifecyclePhase.PreToolUse"/> → <see cref="OnPreToolUseAsync"/>（当前主对话路径已由流式 ToolCallItem 接线）</description></item>
///   <item><description><see cref="MicroChatLifecyclePhase.PostToolUse"/> → <see cref="OnPostToolUseAsync"/>（当前主对话路径已由成功 ToolResultItem 接线）</description></item>
///   <item><description><see cref="MicroChatLifecyclePhase.ToolUseFailure"/> → <see cref="OnToolUseFailureAsync"/>（当前主对话路径已由失败 ToolResultItem 接线）</description></item>
///   <item><description><see cref="MicroChatLifecyclePhase.AfterDispatch"/> → <see cref="OnAfterDispatchAsync"/>（当前版本已触发，且进入阶段前会尝试写入 FinalAssistantMessage）</description></item>
///   <item><description><see cref="MicroChatLifecyclePhase.OnError"/> → <see cref="OnErrorAsync"/>（当前主对话路径已接线）</description></item>
///   <item><description><see cref="MicroChatLifecyclePhase.OnCanceled"/> → <see cref="OnCanceledAsync"/>（当前主对话路径已接线）</description></item>
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
///     protected internal override async ValueTask OnAfterDispatchAsync(MicroChatContext ctx)
///     {
///         if (ctx.FinalAssistantMessage is { } reply &amp;&amp; ctx.Session.Channel is { } channel)
///             await channel.HandleSessionMessageAsync(/* ... */, ctx.Ct);
///     }
/// }
/// </code>
/// </remarks>
public abstract class PetComponent : MicroClaw.Core.MicroComponent
{
    /// <summary>Gets the current host as a <see cref="MicroPet"/> when this component is attached to one.</summary>
    public MicroPet? Pet => Owner as MicroPet;

    /// <summary>Gets the current host as a required <see cref="MicroPet"/>.</summary>
    public MicroPet GetRequiredPet()
    {
        MicroPet? pet = Pet;
        return pet ?? throw new InvalidOperationException($"Pet component '{GetType().Name}' requires a MicroPet host.");
    }

    /// <summary>Gets the session associated with the host <see cref="MicroPet"/>.</summary>
    public IMicroSession MicroSession => GetRequiredPet().MicroSession;

    /// <summary>Gets the session id associated with the host <see cref="MicroPet"/>.</summary>
    public string SessionId => MicroSession.Id;

    /// <summary>Gets a Pet component from the same host.</summary>
    public TComponent? GetPetComponent<TComponent>() where TComponent : PetComponent
    {
        GetRequiredPet();
        return GetComponent<TComponent>();
    }

    /// <summary>Gets a required Pet component from the same host.</summary>
    public TComponent GetRequiredPetComponent<TComponent>() where TComponent : PetComponent
    {
        GetRequiredPet();
        return GetRequiredComponent<TComponent>();
    }

    /// <summary>
    /// 同一 <see cref="MicroChatLifecyclePhase"/> 内的排序权值，越小越先执行，默认 0。
    /// </summary>
    protected internal virtual int Order => 0;

    // ── Decorate 层（per-dispatch，当前实际 Agent 调用路径已触发） ──
    
    /// <summary>
    /// <see cref="MicroChatLifecyclePhase.Decorate"/> 回调：为单个 Agent 请求装配/装饰上下文——
    /// 注入 System Prompt 片段、工具、技能、RAG 文档等。多 Agent 编排时对每个 Agent 各 1 次；
    /// 多个组件按 <see cref="Order"/> 升序合并贡献。默认 no-op。
    /// </summary>
    protected internal virtual ValueTask OnDecorateAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    // ── 派发层（每个 dispatch 1 次） ───────────────────────────────────────────

    /// <summary>
    /// <see cref="MicroChatLifecyclePhase.BeforeDispatch"/> 回调：当前 dispatch 的基础上下文已就位，即将调用
    /// AgentRunner / LLM。目标 contract 下它只观察装配结果；完整 prompt/工具收口仍待后续任务前移。默认 no-op。
    /// </summary>
    protected internal virtual ValueTask OnBeforeDispatchAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    /// <summary>
    /// <see cref="MicroChatLifecyclePhase.AfterDispatch"/> 回调：目标 contract 下，当前 dispatch 的 LLM 往返和工具调用都已完成，
    /// assistant 最终消息已就绪。当前实现里 <see cref="MicroChatContext.FinalAssistantMessage"/> 仍可能为 null；
    /// 典型用途是持久化、回推到 Channel、情绪更新、埋点等。默认 no-op。
    /// </summary>
    protected internal virtual ValueTask OnAfterDispatchAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    // ── 工具层（每次 tool call 1 次，当前主对话路径已接线） ───────────────────────

    /// <summary>
    /// <see cref="MicroChatLifecyclePhase.PreToolUse"/> 回调：LLM 决定调用工具前的拦截/校验/改写时机。
    /// <para>当前主对话路径会在收到流式 <see cref="ToolCallItem"/> 后触发；其它路径仍可能尚未接线。</para>
    /// </summary>
    protected internal virtual ValueTask OnPreToolUseAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    /// <summary>
    /// <see cref="MicroChatLifecyclePhase.PostToolUse"/> 回调：工具成功执行完成后的观察/上报时机。
    /// <para>当前主对话路径会在收到成功的 <see cref="ToolResultItem"/> 后触发；其它路径仍可能尚未接线。</para>
    /// </summary>
    protected internal virtual ValueTask OnPostToolUseAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    /// <summary>
    /// <see cref="MicroChatLifecyclePhase.ToolUseFailure"/> 回调：工具执行抛错后的补偿/降级/上报时机。
    /// <para>当前主对话路径会在收到失败的 <see cref="ToolResultItem"/> 后触发；其它路径仍可能尚未接线。</para>
    /// </summary>
    protected internal virtual ValueTask OnToolUseFailureAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    // ── 异常路径（每 turn 至多 1 次，当前主对话路径已接线） ───────────────────────

    /// <summary>
    /// <see cref="MicroChatLifecyclePhase.OnError"/> 回调：turn 内抛出未被组件吞掉的异常。
    /// <para>当前主对话路径在 dispatch 生命周期已启动后会派发该阶段；未进入 dispatch 的分支不会触发。</para>
    /// </summary>
    protected internal virtual ValueTask OnErrorAsync(MicroChatContext ctx) => ValueTask.CompletedTask;

    /// <summary>
    /// <see cref="MicroChatLifecyclePhase.OnCanceled"/> 回调：turn 被取消。
    /// <para>当前主对话路径在 dispatch 生命周期已启动后会派发该阶段；未进入 dispatch 的分支不会触发。</para>
    /// </summary>
    protected internal virtual ValueTask OnCanceledAsync(MicroChatContext ctx) => ValueTask.CompletedTask;
}
