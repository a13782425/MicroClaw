namespace MicroClaw.Core;

/// <summary>服务生命周期状态。</summary>
public enum MicroServiceState
{
    Stopped,
    Starting,
    Running,
    Stopping,
}

/// <summary>
/// 引擎服务的抽象基类，随引擎启停而启停。
/// 子类重写 <see cref="StartAsync"/> / <see cref="StopAsync"/> 实现具体逻辑。
/// </summary>
public abstract class MicroService
{
    /// <summary>启动顺序，数值越小越先启动（停止时逆序）。</summary>
    public virtual int Order => 0;

    /// <summary>所属引擎，未注册时为 null。</summary>
    public MicroEngine? Engine { get; private set; }

    /// <summary>当前服务状态。</summary>
    public MicroServiceState State { get; private set; } = MicroServiceState.Stopped;

    /// <summary>服务是否处于 Running 状态。</summary>
    public bool IsStarted => State == MicroServiceState.Running;

    internal void AttachToEngine(MicroEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        if (Engine is not null && !ReferenceEquals(Engine, engine))
            throw new InvalidOperationException("A MicroService can only belong to one MicroEngine at a time.");

        Engine = engine;
    }

    internal void DetachFromEngine(MicroEngine engine)
    {
        if (ReferenceEquals(Engine, engine))
            Engine = null;
    }

    internal async ValueTask StartInternalAsync(CancellationToken cancellationToken = default)
    {
        if (State == MicroServiceState.Running)
            return;

        if (State != MicroServiceState.Stopped)
            throw new InvalidOperationException($"MicroService cannot start while it is '{State}'.");

        State = MicroServiceState.Starting;
        await StartAsync(cancellationToken);
        State = MicroServiceState.Running;
    }

    internal async ValueTask StopInternalAsync(CancellationToken cancellationToken = default)
    {
        if (State == MicroServiceState.Stopped || State == MicroServiceState.Stopping)
            return;

        MicroServiceState previousState = State;
        State = MicroServiceState.Stopping;

        try
        {
            await StopAsync(cancellationToken);
            State = MicroServiceState.Stopped;
        }
        catch
        {
            State = previousState;
            throw;
        }
    }

    /// <summary>子类重写此方法以实现启动逻辑；默认为空操作。</summary>
    protected virtual ValueTask StartAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    /// <summary>子类重写此方法以实现停止逻辑；默认为空操作。</summary>
    protected virtual ValueTask StopAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}