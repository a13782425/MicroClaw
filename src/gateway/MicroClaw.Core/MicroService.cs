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
/// 子类优先重写 OnXXX 生命周期方法，也可暂时继续使用 StartAsync / StopAsync 过渡。
/// </summary>
public abstract class MicroService : MicroLifeCycle<MicroEngine>
{
    /// <summary>启动顺序，数值越小越先启动（停止时逆序）。</summary>
    public virtual int Order => 0;

    /// <summary>所属引擎，未注册时为 null。</summary>
    public MicroEngine? Engine => Host;

    /// <summary>当前服务状态。</summary>
    public MicroServiceState State { get; private set; } = MicroServiceState.Stopped;

    /// <summary>服务是否处于 Running 状态。</summary>
    public bool IsStarted => State == MicroServiceState.Running && LifeCycleState == MicroLifeCycleState.Active;

    public override void Dispose()
    {
        if (Engine is { } engine)
        {
            engine.DisposeService(this);
            return;
        }

        DisposeCore();
    }

    internal void AttachToEngine(MicroEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        if (Host is not null && !ReferenceEquals(Host, engine))
            throw new InvalidOperationException("A MicroService can only belong to one MicroEngine at a time.");

        AttachToHost(engine);
    }

    internal void DetachFromEngine(MicroEngine engine)
    {
        if (ReferenceEquals(Host, engine))
        {
            DetachCore();
            if (State != MicroServiceState.Starting)
                State = MicroServiceState.Stopped;
        }
    }

    internal override async ValueTask StartNodeAsync(CancellationToken cancellationToken = default)
    {
        if (State == MicroServiceState.Running)
            return;

        if (State != MicroServiceState.Stopped)
            throw new InvalidOperationException($"MicroService cannot start while it is '{State}'.");

        State = MicroServiceState.Starting;
        ResetActivationHookTracking();
        WriteTrace($"{GetType().Name} entering start sequence.");

        await base.StartNodeAsync(cancellationToken);

        State = MicroServiceState.Running;
        WriteTrace($"{GetType().Name} is running.");
    }

    internal override async ValueTask StopNodeAsync(CancellationToken cancellationToken = default)
    {
        if (State == MicroServiceState.Stopped)
            return;

        MicroServiceState previousState = State;
        if (State != MicroServiceState.Stopping)
            State = MicroServiceState.Stopping;

        WriteTrace($"{GetType().Name} entering stop sequence.");

        List<Exception> errors = [];

        try
        {
            if (LifeCycleState == MicroLifeCycleState.Active)
            {
                try
                {
                    await base.StopNodeAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            if (previousState == MicroServiceState.Starting && ActivationHookEntered && LifeCycleState == MicroLifeCycleState.Initialized)
            {
                try
                {
                    await OnDeactivatedAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            if (LifeCycleState == MicroLifeCycleState.Initialized)
            {
                try
                {
                    await UninitializeCoreAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            if (errors.Count == 0)
            {
                State = MicroServiceState.Stopped;
                ResetActivationHookTracking();
                WriteTrace($"{GetType().Name} stopped.");
            }
            else
            {
                State = LifeCycleState == MicroLifeCycleState.Active ? previousState : MicroServiceState.Stopping;
                WriteTrace($"{GetType().Name} failed while stopping.");
            }
        }
        catch
        {
            State = LifeCycleState == MicroLifeCycleState.Active ? previousState : MicroServiceState.Stopping;
            WriteTrace($"{GetType().Name} failed while stopping.");
            throw;
        }

        ThrowIfNeeded(errors);
    }

    internal void DisposeCore()
    {
        if (IsDisposed)
            return;

        List<Exception> errors = [];
        bool stopFailed = false;

        try
        {
            StopNodeAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            stopFailed = true;
            errors.Add(ex);
        }

        if (stopFailed && LifeCycleState == MicroLifeCycleState.Initialized)
        {
            try
            {
                UninitializeCoreAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        try
        {
            DisposeLifeCycle(skipDetachRollback: stopFailed);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }

        State = MicroServiceState.Stopped;
        ResetActivationHookTracking();
        ThrowIfNeeded(errors);
    }

    protected override ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
        => StartAsync(cancellationToken);

    protected override ValueTask OnDeactivatedAsync(CancellationToken cancellationToken = default)
        => StopAsync(cancellationToken);

    /// <summary>子类重写此方法以实现启动逻辑；默认为空操作。</summary>
    protected virtual ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>子类重写此方法以实现停止逻辑；默认为空操作。</summary>
    protected virtual ValueTask StopAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    private static void WriteTrace(string message)
    {
        System.Diagnostics.Trace.WriteLine($"[MicroService] {message}");
    }

    private static void ThrowIfNeeded(IReadOnlyList<Exception> errors)
    {
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();

        if (errors.Count > 1)
            throw new AggregateException(errors);
    }
}