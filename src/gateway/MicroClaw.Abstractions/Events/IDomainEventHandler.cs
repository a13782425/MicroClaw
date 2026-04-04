namespace MicroClaw.Abstractions.Events;

/// <summary>
/// 领域事件处理器接口。针对特定事件类型 <typeparamref name="TEvent"/> 的异步处理器。
/// </summary>
/// <typeparam name="TEvent">需要处理的领域事件类型，须实现 <see cref="IDomainEvent"/>。</typeparam>
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    /// <summary>处理指定领域事件。</summary>
    /// <param name="domainEvent">触发的领域事件实例。</param>
    /// <param name="ct">取消令牌。</param>
    Task HandleAsync(TEvent domainEvent, CancellationToken ct = default);
}
