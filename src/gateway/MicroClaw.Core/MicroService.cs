namespace MicroClaw.Core;

/// <summary>服务运行状态。</summary>
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
    private readonly SemaphoreSlim _serviceTransitionGate = new(1, 1);
    private readonly AsyncLocal<ServiceTransitionScopeState?> _serviceTransitionScope = new();

    /// <summary>启动顺序，数值越小越先启动（停止时逆序）。</summary>
    public virtual int Order => 0;

    /// <summary>所属引擎，未注册时为 null。</summary>
    public MicroEngine? Engine => Host;

    /// <summary>当前服务运行状态。</summary>
    public MicroServiceState State { get; private set; } = MicroServiceState.Stopped;

    /// <summary>服务是否处于 Running 且 Active 状态。</summary>
    public bool IsStarted => State == MicroServiceState.Running && LifeCycleState == MicroLifeCycleState.Active;

    /// <summary>释放服务；若已注册则交给引擎统一销毁。</summary>
    public override async ValueTask DisposeAsync()
    {
        if (Engine is { } engine)
        {
            await engine.DisposeServiceAsync(this);
            return;
        }

        await DisposeCoreAsync();
    }

    /// <summary>将服务挂接到指定引擎。</summary>
    internal async ValueTask AttachToEngineAsync(MicroEngine engine, CancellationToken cancellationToken = default)
    {
        await WaitForServiceTransitionGateIfNeededAsync(cancellationToken);
        ServiceTransitionScopeState scope = EnterServiceTransitionScope();
        try
        {
            ArgumentNullException.ThrowIfNull(engine);

            if (Host is not null && !ReferenceEquals(Host, engine))
                throw new InvalidOperationException("A MicroService can only belong to one MicroEngine at a time.");

            await AttachToHostAsync(engine, cancellationToken);
        }
        finally
        {
            ExitServiceTransitionScope(scope);
        }
    }

    /// <summary>将服务从指定引擎分离。</summary>
    internal async ValueTask DetachFromEngineAsync(MicroEngine engine, CancellationToken cancellationToken = default)
    {
        await WaitForServiceTransitionGateIfNeededAsync(cancellationToken);
        ServiceTransitionScopeState scope = EnterServiceTransitionScope();
        try
        {
            if (ReferenceEquals(Host, engine))
            {
                await DetachCoreAsync(cancellationToken: cancellationToken);
                if (State != MicroServiceState.Starting)
                    State = MicroServiceState.Stopped;
            }
        }
        finally
        {
            ExitServiceTransitionScope(scope);
        }
    }

    /// <summary>执行服务启动阶段的完整生命周期推进。</summary>
    internal override async ValueTask StartNodeAsync(CancellationToken cancellationToken = default)
    {
        await WaitForServiceTransitionGateIfNeededAsync(cancellationToken);
        ServiceTransitionScopeState scope = EnterServiceTransitionScope();
        try
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
        finally
        {
            ExitServiceTransitionScope(scope);
        }
    }

    /// <summary>执行服务停止阶段的完整生命周期回退。</summary>
    internal override async ValueTask StopNodeAsync(CancellationToken cancellationToken = default)
    {
        await WaitForServiceTransitionGateIfNeededAsync(cancellationToken);
        ServiceTransitionScopeState scope = EnterServiceTransitionScope();
        try
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
        finally
        {
            ExitServiceTransitionScope(scope);
        }
    }

    /// <summary>在不回调引擎的情况下直接销毁服务本体。</summary>
    internal async ValueTask DisposeCoreAsync()
    {
        await WaitForServiceTransitionGateIfNeededAsync(CancellationToken.None);
        ServiceTransitionScopeState scope = EnterServiceTransitionScope();
        try
        {
            if (IsDisposed)
                return;

            List<Exception> errors = [];
            bool stopFailed = false;

            try
            {
                await StopNodeAsync(CancellationToken.None);
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
                    await UninitializeCoreAsync(CancellationToken.None);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            try
            {
                await DisposeLifeCycleAsync(skipDetachRollback: stopFailed);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }

            State = MicroServiceState.Stopped;
            ResetActivationHookTracking();
            ThrowIfNeeded(errors);
        }
        finally
        {
            ExitServiceTransitionScope(scope);
        }
    }

    /// <summary>在当前异步流未持有作用域时等待服务运行态转换门。</summary>
    private async ValueTask WaitForServiceTransitionGateIfNeededAsync(CancellationToken cancellationToken)
    {
        if (_serviceTransitionScope.Value is { IsActive: true })
            return;

        await _serviceTransitionGate.WaitAsync(cancellationToken);
    }

    /// <summary>进入当前异步流的服务运行态转换作用域。</summary>
    private ServiceTransitionScopeState EnterServiceTransitionScope()
    {
        ServiceTransitionScopeState? scope = _serviceTransitionScope.Value;
        if (scope is { IsActive: true })
        {
            scope.Depth++;
            return scope;
        }

        scope = new ServiceTransitionScopeState
        {
            Depth = 1,
        };
        _serviceTransitionScope.Value = scope;
        return scope;
    }

    /// <summary>退出服务运行态转换作用域并按需释放实例级门禁。</summary>
    private void ExitServiceTransitionScope(ServiceTransitionScopeState scope)
    {
        if (!scope.IsActive || scope.Depth <= 0)
            throw new InvalidOperationException("MicroService transition scope is not active.");

        scope.Depth--;
        if (scope.Depth == 0)
        {
            scope.IsActive = false;
            if (ReferenceEquals(_serviceTransitionScope.Value, scope))
                _serviceTransitionScope.Value = null;

            _serviceTransitionGate.Release();
        }
    }

    /// <summary>在生命周期激活阶段转发到服务启动逻辑。</summary>
    protected override ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
        => StartAsync(cancellationToken);

    /// <summary>在生命周期停用阶段转发到服务停止逻辑。</summary>
    protected override ValueTask OnDeactivatedAsync(CancellationToken cancellationToken = default)
        => StopAsync(cancellationToken);

    /// <summary>子类重写此方法以实现启动逻辑；默认为空操作。</summary>
    protected virtual ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>子类重写此方法以实现停止逻辑；默认为空操作。</summary>
    protected virtual ValueTask StopAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    
    /// <summary>描述当前异步流持有的服务运行态转换作用域。</summary>
    private sealed class ServiceTransitionScopeState
    {
        public int Depth { get; set; }

        public bool IsActive { get; set; } = true;
    }
}