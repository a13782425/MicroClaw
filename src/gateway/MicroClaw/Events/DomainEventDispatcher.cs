using MicroClaw.Abstractions.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Events;

/// <summary>
/// 基于 DI 的领域事件分发器（Singleton）。
/// <para>
/// 通过 <see cref="IServiceProvider.GetServices{T}"/> 解析所有已注册的
/// <see cref="IDomainEventHandler{TEvent}"/> 实例，顺序调用每个处理器。
/// 单个处理器抛出的异常被捕获并记录警告日志，不中断主流程。
/// </para>
/// </summary>
public sealed class DomainEventDispatcher(
    IServiceProvider serviceProvider,
    ILogger<DomainEventDispatcher> logger)
    : IDomainEventDispatcher
{
    public async Task DispatchAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default)
        where TEvent : IDomainEvent
    {
        var handlers = serviceProvider.GetServices<IDomainEventHandler<TEvent>>();
        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(domainEvent, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "领域事件处理器 {HandlerType} 处理 {EventType} 时抛出异常，已跳过",
                    handler.GetType().Name,
                    typeof(TEvent).Name);
            }
        }
    }
}
