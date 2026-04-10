namespace MicroClaw.Abstractions.Events;

/// <summary>
/// 领域事件分发器。负责将领域事件分发给所有已注册的处理器。
/// </summary>
/// <remarks>已过时，请改用 <see cref="IAsyncEventBus"/>。</remarks>
[Obsolete("请改用 IAsyncEventBus 发布/订阅事件，IDomainEventDispatcher 将在后续版本移除。")]
public interface IDomainEventDispatcher
{
    /// <summary>
    /// 分发领域事件给所有已注册的 <see cref="IDomainEventHandler{TEvent}"/>。
    /// <para>
    /// 顺序同步调用所有处理器；单个处理器抛出的异常被捕获并记录日志，
    /// 不中断主流程，也不影响其余处理器的执行。
    /// </para>
    /// </summary>
    /// <typeparam name="TEvent">领域事件类型。</typeparam>
    /// <param name="domainEvent">待分发的事件实例。</param>
    /// <param name="ct">取消令牌。</param>
    Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
        where TEvent : IDomainEvent;
}
