using MicroClaw.Abstractions.Events;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Events;

/// <summary>
/// 基于进程内字典的 <see cref="IAsyncEventBus"/> 实现，以单例注册。
/// <para>
/// Subscribe 通常在启动阶段调用，PublishAsync 在运行时调用，
/// 使用 <see cref="Lock"/> 保证 handler 列表的读写安全。
/// </para>
/// </summary>
internal sealed class InMemoryAsyncEventBus(ILogger<InMemoryAsyncEventBus> logger) : IAsyncEventBus
{
    private readonly Dictionary<Type, List<Func<object, CancellationToken, Task>>> _handlers = new();
    private readonly Lock _lock = new();

    public void Subscribe<T>(Func<T, CancellationToken, Task> handler) where T : class
    {
        lock (_lock)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
                _handlers[typeof(T)] = list = [];
            list.Add((e, ct) => handler((T)e, ct));
        }
    }

    public async Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : class
    {
        List<Func<object, CancellationToken, Task>>? snapshot;
        lock (_lock)
            _handlers.TryGetValue(typeof(T), out snapshot);

        if (snapshot is null) return;

        foreach (var handler in snapshot)
        {
            try
            {
                await handler(@event, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "事件处理器处理 {EventType} 时抛出异常，已跳过", typeof(T).Name);
            }
        }
    }
}
