using MicroClaw.Core.Logging;

namespace MicroClaw.Core;

/// <summary>通用生命周期状态。</summary>
public enum MicroLifeCycleState
{
    Detached,
    Attached,
    Initialized,
    Active,
    Disposed,
}

/// <summary>为对象、组件和服务提供统一生命周期管理的抽象基类。</summary>
public abstract class MicroLifeCycle<THost> : IAsyncDisposable where THost : class
{
    private readonly SemaphoreSlim _transitionGate = new(1, 1);
    private readonly AsyncLocal<TransitionScopeState?> _transitionScope = new();
    private bool _activationHookEntered;
    private IMicroLogger? _logger;
    private bool _traceEnabled;
    private bool _traceEnabledEvaluated;

    /// <summary>
    /// 当前生命周期节点的 logger，分类名取自运行时类型。惰性初始化以便宿主在启动阶段
    /// 替换 <see cref="MicroLogger.Factory"/> 后仍能被后续实例拾取到。
    /// 使用 <see cref="LazyInitializer.EnsureInitialized{T}(ref T,Func{T})"/> 保证并发安全：
    /// 即使多个线程同时首次访问，最终也只有一个 <see cref="IMicroLogger"/> 实例胜出并被缓存。
    /// </summary>
    protected IMicroLogger Logger => LazyInitializer.EnsureInitialized(ref _logger, CreateLogger);

    private IMicroLogger CreateLogger() => MicroLogger.Factory.CreateLogger(GetType());

    /// <summary>当前关联的宿主对象。</summary>
    public THost? Host { get; private set; }

    /// <summary>当前内部生命周期状态。</summary>
    public MicroLifeCycleState LifeCycleState { get; private set; } = MicroLifeCycleState.Detached;

    /// <summary>是否已经挂接到宿主。</summary>
    public bool IsAttached => Host is not null;

    /// <summary>是否已经完成初始化。</summary>
    public bool IsInitialized => LifeCycleState is MicroLifeCycleState.Initialized or MicroLifeCycleState.Active;

    /// <summary>是否已经进入激活状态。</summary>
    public bool IsActive => LifeCycleState == MicroLifeCycleState.Active;

    /// <summary>是否已经完成释放。</summary>
    public bool IsDisposed => LifeCycleState == MicroLifeCycleState.Disposed;

    /// <summary>激活钩子是否已经进入执行。</summary>
    protected bool ActivationHookEntered => _activationHookEntered;

    /// <summary>直接设置内部生命周期状态。</summary>
    protected void SetLifeCycleState(MicroLifeCycleState state)
        => LifeCycleState = state;

    /// <summary>推进节点完成启动流程。</summary>
    internal virtual ValueTask StartNodeAsync(CancellationToken cancellationToken = default)
        => StartNodeAsyncCore(cancellationToken);

    /// <summary>推进节点完成停止流程。</summary>
    internal virtual ValueTask StopNodeAsync(CancellationToken cancellationToken = default)
        => StopNodeAsyncCore(cancellationToken);

    /// <summary>将当前节点挂接到指定宿主。</summary>
    internal async ValueTask AttachToHostAsync(THost host, CancellationToken cancellationToken = default)
    {
        await WaitForTransitionGateIfNeededAsync(cancellationToken);
        TransitionScopeState scope = EnterTransitionScope();
        try
        {
            ArgumentNullException.ThrowIfNull(host);
            EnsureNotDisposed();

            if (Host is not null && !ReferenceEquals(Host, host))
                throw new InvalidOperationException("A lifecycle node can only belong to one host at a time.");

            if (ReferenceEquals(Host, host))
                return;

            Host = host;
            LifeCycleState = MicroLifeCycleState.Attached;
            WriteTrace($"{GetType().Name} attached to {typeof(THost).Name}.");

            try
            {
                await OnAttachedAsync(cancellationToken);
            }
            catch
            {
                Host = null;
                LifeCycleState = MicroLifeCycleState.Detached;
                WriteTrace($"{GetType().Name} failed while attaching to {typeof(THost).Name}.");
                throw;
            }
        }
        finally
        {
            ExitTransitionScope(scope);
        }
    }

    /// <summary>将当前节点推进到已初始化状态。</summary>
    internal async ValueTask InitializeCoreAsync(CancellationToken cancellationToken = default)
    {
        await WaitForTransitionGateIfNeededAsync(cancellationToken);
        TransitionScopeState scope = EnterTransitionScope();
        try
        {
            EnsureNotDisposed();

            if (LifeCycleState is MicroLifeCycleState.Initialized or MicroLifeCycleState.Active)
                return;

            EnsureAttached();

            MicroLifeCycleState previousState = LifeCycleState;
            LifeCycleState = MicroLifeCycleState.Initialized;
            WriteTrace($"{GetType().Name} initialized.");

            try
            {
                await OnInitializedAsync(cancellationToken);
            }
            catch
            {
                LifeCycleState = previousState;
                WriteTrace($"{GetType().Name} failed during initialization.");
                throw;
            }
        }
        finally
        {
            ExitTransitionScope(scope);
        }
    }

    /// <summary>将当前节点推进到激活状态。</summary>
    internal async ValueTask ActivateCoreAsync(CancellationToken cancellationToken = default)
    {
        await WaitForTransitionGateIfNeededAsync(cancellationToken);
        TransitionScopeState scope = EnterTransitionScope();
        try
        {
            EnsureNotDisposed();

            if (LifeCycleState == MicroLifeCycleState.Active)
                return;

            await InitializeCoreAsync(cancellationToken);

            MicroLifeCycleState previousState = LifeCycleState;

            try
            {
                _activationHookEntered = true;
                await OnActivatedAsync(cancellationToken);
                LifeCycleState = MicroLifeCycleState.Active;
                WriteTrace($"{GetType().Name} activated.");
            }
            catch
            {
                LifeCycleState = previousState;
                    ResetActivationHookTracking();
                WriteTrace($"{GetType().Name} failed during activation.");
                throw;
            }
        }
        finally
        {
            ExitTransitionScope(scope);
        }
    }

    /// <summary>将当前节点从激活状态回退到已初始化状态。</summary>
    internal async ValueTask DeactivateCoreAsync(CancellationToken cancellationToken = default)
    {
        await WaitForTransitionGateIfNeededAsync(cancellationToken);
        TransitionScopeState scope = EnterTransitionScope();
        try
        {
            if (LifeCycleState != MicroLifeCycleState.Active)
                return;

            try
            {
                await OnDeactivatedAsync(cancellationToken);
                LifeCycleState = MicroLifeCycleState.Initialized;
                WriteTrace($"{GetType().Name} deactivated.");
            }
            catch
            {
                LifeCycleState = MicroLifeCycleState.Active;
                WriteTrace($"{GetType().Name} failed during deactivation.");
                throw;
            }
        }
        finally
        {
            ExitTransitionScope(scope);
        }
    }

    /// <summary>将当前节点从已初始化状态回退到已挂接状态。</summary>
    internal async ValueTask UninitializeCoreAsync(CancellationToken cancellationToken = default)
    {
        await WaitForTransitionGateIfNeededAsync(cancellationToken);
        TransitionScopeState scope = EnterTransitionScope();
        try
        {
            if (LifeCycleState != MicroLifeCycleState.Initialized)
                return;

            try
            {
                await OnUninitializedAsync(cancellationToken);
                LifeCycleState = MicroLifeCycleState.Attached;
                WriteTrace($"{GetType().Name} uninitialized.");
            }
            catch
            {
                LifeCycleState = MicroLifeCycleState.Initialized;
                WriteTrace($"{GetType().Name} failed during uninitialization.");
                throw;
            }
        }
        finally
        {
            ExitTransitionScope(scope);
        }
    }

    /// <summary>将当前节点从宿主上分离，并按需回滚生命周期。</summary>
    internal async ValueTask DetachCoreAsync(bool skipRollback = false, CancellationToken cancellationToken = default)
    {
        await WaitForTransitionGateIfNeededAsync(cancellationToken);
        TransitionScopeState scope = EnterTransitionScope();
        try
        {
            if (Host is null)
                return;

            List<Exception> errors = [];

            if (!skipRollback && LifeCycleState == MicroLifeCycleState.Active)
                await RollbackToInitializedAsync(errors, cancellationToken);

            if (!skipRollback && LifeCycleState == MicroLifeCycleState.Initialized)
                await RollbackToAttachedAsync(errors, cancellationToken);

            try
            {
                await OnDetachedAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
            finally
            {
                Host = null;
                LifeCycleState = MicroLifeCycleState.Detached;
            }

            WriteTrace($"{GetType().Name} detached.");
            ThrowIfNeeded(errors);
        }
        finally
        {
            ExitTransitionScope(scope);
        }
    }

    /// <summary>将当前节点回滚或推进到目标生命周期状态。</summary>
    internal async ValueTask RollbackToCoreAsync(MicroLifeCycleState state, CancellationToken cancellationToken = default)
    {
        await WaitForTransitionGateIfNeededAsync(cancellationToken);
        TransitionScopeState scope = EnterTransitionScope();
        try
        {
            List<Exception> errors = [];

            switch (state)
            {
                case MicroLifeCycleState.Detached:
                    if (Host is not null)
                    {
                        try
                        {
                            await DetachCoreAsync(cancellationToken: cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            errors.Add(ex);
                        }
                    }
                    break;
                case MicroLifeCycleState.Attached:
                    if (LifeCycleState == MicroLifeCycleState.Active)
                        await RollbackToInitializedAsync(errors, cancellationToken);
                    if (LifeCycleState == MicroLifeCycleState.Initialized)
                        await RollbackToAttachedAsync(errors, cancellationToken);
                    break;
                case MicroLifeCycleState.Initialized:
                    if (LifeCycleState == MicroLifeCycleState.Active)
                        await RollbackToInitializedAsync(errors, cancellationToken);
                    if (LifeCycleState == MicroLifeCycleState.Attached)
                    {
                        try
                        {
                            await InitializeCoreAsync(cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            errors.Add(ex);
                        }
                    }
                    break;
                case MicroLifeCycleState.Active:
                    if (LifeCycleState != MicroLifeCycleState.Active)
                    {
                        try
                        {
                            await ActivateCoreAsync(cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            errors.Add(ex);
                        }
                    }
                    break;
                case MicroLifeCycleState.Disposed:
                    try
                    {
                        await DisposeAsync();
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                    break;
            }

            ThrowIfNeeded(errors);
        }
        finally
        {
            ExitTransitionScope(scope);
        }
    }

    /// <summary>按启动顺序执行初始化和激活。</summary>
    private async ValueTask StartNodeAsyncCore(CancellationToken cancellationToken)
    {
        await InitializeCoreAsync(cancellationToken);
        await ActivateCoreAsync(cancellationToken);
    }

    /// <summary>按停止顺序执行停用。</summary>
    private async ValueTask StopNodeAsyncCore(CancellationToken cancellationToken)
    {
        await DeactivateCoreAsync(cancellationToken);
    }

    /// <summary>释放当前生命周期节点。</summary>
    public virtual ValueTask DisposeAsync()
        => DisposeLifeCycleAsync();

    /// <summary>执行完整的生命周期释放与分离逻辑。</summary>
    internal async ValueTask DisposeLifeCycleAsync(bool skipDetachRollback = false, CancellationToken cancellationToken = default)
    {
        await WaitForTransitionGateIfNeededAsync(cancellationToken);
        TransitionScopeState scope = EnterTransitionScope();
        try
        {
            if (LifeCycleState == MicroLifeCycleState.Disposed)
                return;

            List<Exception> errors = [];

            if (Host is not null)
            {
                try
                {
                    await DetachCoreAsync(skipDetachRollback, cancellationToken);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            try
            {
                await OnDisposedAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
            finally
            {
                Host = null;
                LifeCycleState = MicroLifeCycleState.Disposed;
            }

            WriteTrace($"{GetType().Name} disposed.");
            ThrowIfNeeded(errors);
        }
        finally
        {
            ExitTransitionScope(scope);
        }
    }

    /// <summary>在当前异步流未持有作用域时等待生命周期转换门。</summary>
    private async ValueTask WaitForTransitionGateIfNeededAsync(CancellationToken cancellationToken)
    {
        if (_transitionScope.Value is { IsActive: true })
            return;

        await _transitionGate.WaitAsync(cancellationToken);
    }

    /// <summary>进入当前异步流的生命周期转换作用域。</summary>
    private TransitionScopeState EnterTransitionScope()
    {
        TransitionScopeState? scope = _transitionScope.Value;
        if (scope is { IsActive: true })
        {
            scope.Depth++;
            return scope;
        }

        scope = new TransitionScopeState
        {
            Depth = 1,
        };
        _transitionScope.Value = scope;
        return scope;
    }

    /// <summary>退出生命周期转换作用域并按需释放实例级门禁。</summary>
    private void ExitTransitionScope(TransitionScopeState scope)
    {
        if (!scope.IsActive || scope.Depth <= 0)
            throw new InvalidOperationException("MicroLifeCycle transition scope is not active.");

        scope.Depth--;
        if (scope.Depth == 0)
        {
            scope.IsActive = false;
            if (ReferenceEquals(_transitionScope.Value, scope))
                _transitionScope.Value = null;

            _transitionGate.Release();
        }
    }

    /// <summary>获取当前必定存在的宿主对象。</summary>
    protected THost GetRequiredHost()
        => Host ?? throw new InvalidOperationException("This lifecycle node is not attached to a host.");

    /// <summary>重置激活钩子进入标记。</summary>
    protected void ResetActivationHookTracking()
        => _activationHookEntered = false;

    /// <summary>挂接完成后的扩展钩子。</summary>
    protected virtual ValueTask OnAttachedAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>初始化完成前的扩展钩子。</summary>
    protected virtual ValueTask OnInitializedAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>激活阶段的扩展钩子。</summary>
    protected virtual ValueTask OnActivatedAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>停用阶段的扩展钩子。</summary>
    protected virtual ValueTask OnDeactivatedAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>反初始化阶段的扩展钩子。</summary>
    protected virtual ValueTask OnUninitializedAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>分离阶段的扩展钩子。</summary>
    protected virtual ValueTask OnDetachedAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>释放阶段的扩展钩子。</summary>
    protected virtual ValueTask OnDisposedAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>确保当前节点已经挂接到宿主。</summary>
    private void EnsureAttached()
    {
        if (Host is null)
            throw new InvalidOperationException("This lifecycle node must be attached to a host before it can be initialized.");
    }

    /// <summary>确保当前节点尚未被释放。</summary>
    private void EnsureNotDisposed()
    {
        if (LifeCycleState == MicroLifeCycleState.Disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    /// <summary>尝试将节点回滚到已初始化状态，并记录失败。</summary>
    private async ValueTask RollbackToInitializedAsync(List<Exception> errors, CancellationToken cancellationToken)
    {
        try
        {
            await OnDeactivatedAsync(cancellationToken);
            LifeCycleState = MicroLifeCycleState.Initialized;
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    /// <summary>尝试将节点回滚到已挂接状态，并记录失败。</summary>
    private async ValueTask RollbackToAttachedAsync(List<Exception> errors, CancellationToken cancellationToken)
    {
        try
        {
            await OnUninitializedAsync(cancellationToken);
            LifeCycleState = MicroLifeCycleState.Attached;
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    /// <summary>按数量重新抛出单个或聚合异常。</summary>
    protected static void ThrowIfNeeded(IReadOnlyList<Exception> errors)
    {
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();

        if (errors.Count > 1)
            throw new AggregateException(errors);
    }

    /// <summary>
    /// 写入生命周期级跟踪日志。通过缓存 <see cref="_traceEnabled"/> 快通路规避热路径上每次
    /// 访问 <see cref="Logger"/> 属性的开销；当外部替换了 <see cref="MicroLogger.Factory"/>
    /// 使跟踪级别首次可用时，通过 <see cref="_traceEnabledEvaluated"/> 的一次性求值保证一致性。
    /// </summary>
    protected void WriteTrace(string message)
    {
        if (!IsTraceEnabled())
            return;

        Logger.LogDebug(message);
    }

    private bool IsTraceEnabled()
    {
        if (_traceEnabledEvaluated)
            return _traceEnabled;

        IMicroLogger logger = Logger;
        bool enabled = logger.IsEnabled(MicroLogLevel.Debug);
        _traceEnabled = enabled;
        Volatile.Write(ref _traceEnabledEvaluated, true);
        return enabled;
    }

    /// <summary>描述当前异步流持有的生命周期转换作用域。</summary>
    private sealed class TransitionScopeState
    {
        public int Depth { get; set; }

        public bool IsActive { get; set; } = true;
    }
}