namespace MicroClaw.Abstractions.Events;

/// <summary>
/// 全局异步事件总线。支持进程内跨模块发布和订阅事件，无需暴露具体实现类型。
/// <para>
/// 以单例注册，发布时等待所有已订阅处理器顺序执行完毕后返回。
/// 单个处理器抛出的异常会被捕获并记录，不中断其余处理器的执行。
/// </para>
/// </summary>
public interface IAsyncEventBus
{
    /// <summary>订阅指定事件类型的处理器。通常在服务初始化阶段（构造函数或 StartAsync）调用。</summary>
    /// <typeparam name="T">事件类型。</typeparam>
    /// <param name="handler">处理器委托，接收事件实例与取消令牌，返回 Task。</param>
    void Subscribe<T>(Func<T, CancellationToken, Task> handler) where T : class;

    /// <summary>发布事件，顺序等待所有已订阅处理器完成后返回。</summary>
    /// <typeparam name="T">事件类型。</typeparam>
    /// <param name="event">事件实例。</param>
    /// <param name="ct">取消令牌。</param>
    Task PublishAsync<T>(T @event, CancellationToken ct = default) where T : class;
}
