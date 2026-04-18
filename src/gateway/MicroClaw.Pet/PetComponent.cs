using MicroClaw.Abstractions.Pet;

namespace MicroClaw.Pet;

/// <summary>
/// Pet 组件的抽象基类：继承自 <see cref="MicroClaw.Core.MicroComponent"/>，挂在 <see cref="MicroPet"/>
/// （本身是 <see cref="MicroClaw.Core.MicroObject"/>）上。
/// <para>
/// 子类通过覆写 <see cref="OnBeforeChatAsync"/> / <see cref="OnAfterChatAsync"/> /
/// <see cref="OnBeforeToolCallAsync"/> / <see cref="OnAfterToolCallAsync"/> 把行为注入到
/// <see cref="MicroPet"/> 对话生命周期的对应阶段；默认实现为 no-op，未覆写的 Phase 零开销。
/// </para>
/// <para>
/// 同一 <see cref="ChatLifecyclePhase"/> 内多个组件按 <see cref="Order"/> 升序回调；
/// 相同 <see cref="Order"/> 的执行顺序未定义，组件应避免冲突。
/// </para>
/// </summary>
/// <remarks>
/// <para>典型子类用法（尚未在本轮落地，仅做参考示例）：</para>
/// <code>
/// /// &lt;summary&gt;
/// /// 在 AfterChat 阶段将 assistant 最终消息回推到 Channel。
/// /// &lt;/summary&gt;
/// public sealed class SessionForwardComponent : PetComponent
/// {
///     protected internal override int Order =&gt; 100;
///
///     protected internal override async ValueTask OnAfterChatAsync(ChatContext ctx)
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

    /// <summary>
    /// <see cref="ChatLifecyclePhase.BeforeChat"/> 阶段回调：发起 LLM 调用前的时机（注入上下文、限流、装填 RAG 等）。
    /// 默认 no-op。
    /// </summary>
    protected internal virtual ValueTask OnBeforeChatAsync(ChatContext ctx) => ValueTask.CompletedTask;

    /// <summary>
    /// <see cref="ChatLifecyclePhase.AfterChat"/> 阶段回调：所有 LLM 往返结束、流已耗尽后的出口时机
    /// （持久化、转发到 Channel、情绪更新、埋点等）。默认 no-op。
    /// </summary>
    protected internal virtual ValueTask OnAfterChatAsync(ChatContext ctx) => ValueTask.CompletedTask;

    /// <summary>
    /// <see cref="ChatLifecyclePhase.BeforeToolCall"/> 阶段回调：LLM 决定调用工具前的拦截/校验/改写时机。
    /// <para>当前版本 <see cref="MicroPet"/> 编排器尚未把工具钩子接入，此回调不会被触发。</para>
    /// </summary>
    protected internal virtual ValueTask OnBeforeToolCallAsync(ChatContext ctx) => ValueTask.CompletedTask;

    /// <summary>
    /// <see cref="ChatLifecyclePhase.AfterToolCall"/> 阶段回调：工具执行完成后的观察/上报时机。
    /// <para>当前版本同上，尚未接线。</para>
    /// </summary>
    protected internal virtual ValueTask OnAfterToolCallAsync(ChatContext ctx) => ValueTask.CompletedTask;
}
