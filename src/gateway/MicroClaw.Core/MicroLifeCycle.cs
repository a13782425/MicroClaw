namespace MicroClaw.Core;

public enum MicroLifeCycleState
{
    Detached,
    Attached,
    Initialized,
    Active,
    Disposed,
}

public abstract class MicroLifeCycle<THost> : IDisposable where THost : class
{
    private bool _activationHookEntered;

    public THost? Host { get; private set; }

    public MicroLifeCycleState LifeCycleState { get; private set; } = MicroLifeCycleState.Detached;

    public bool IsAttached => Host is not null;

    public bool IsInitialized => LifeCycleState is MicroLifeCycleState.Initialized or MicroLifeCycleState.Active;

    public bool IsActive => LifeCycleState == MicroLifeCycleState.Active;

    public bool IsDisposed => LifeCycleState == MicroLifeCycleState.Disposed;

    protected bool ActivationHookEntered => _activationHookEntered;

    protected void SetLifeCycleState(MicroLifeCycleState state)
        => LifeCycleState = state;

    internal virtual ValueTask StartNodeAsync(CancellationToken cancellationToken = default)
        => StartNodeAsyncCore(cancellationToken);

    internal virtual ValueTask StopNodeAsync(CancellationToken cancellationToken = default)
        => StopNodeAsyncCore(cancellationToken);

    internal virtual ValueTask TickNodeAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        => TickCoreAsync(deltaTime, cancellationToken);

    internal void AttachToHost(THost host)
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
            OnAttachedAsync().GetAwaiter().GetResult();
        }
        catch
        {
            Host = null;
            LifeCycleState = MicroLifeCycleState.Detached;
            WriteTrace($"{GetType().Name} failed while attaching to {typeof(THost).Name}.");
            throw;
        }
    }

    internal void InitializeCore()
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
            OnInitializedAsync().GetAwaiter().GetResult();
        }
        catch
        {
            LifeCycleState = previousState;
            WriteTrace($"{GetType().Name} failed during initialization.");
            throw;
        }
    }

    internal async ValueTask InitializeCoreAsync(CancellationToken cancellationToken = default)
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

    internal void ActivateCore()
    {
        EnsureNotDisposed();

        if (LifeCycleState == MicroLifeCycleState.Active)
            return;

        InitializeCore();

        MicroLifeCycleState previousState = LifeCycleState;
        LifeCycleState = MicroLifeCycleState.Active;
        WriteTrace($"{GetType().Name} activated.");

        try
        {
            _activationHookEntered = true;
            OnActivatedAsync().GetAwaiter().GetResult();
        }
        catch
        {
            LifeCycleState = previousState;
            WriteTrace($"{GetType().Name} failed during activation.");
            throw;
        }
    }

    internal async ValueTask ActivateCoreAsync(CancellationToken cancellationToken = default)
    {
        EnsureNotDisposed();

        if (LifeCycleState == MicroLifeCycleState.Active)
            return;

        await InitializeCoreAsync(cancellationToken);

        MicroLifeCycleState previousState = LifeCycleState;
        LifeCycleState = MicroLifeCycleState.Active;
        WriteTrace($"{GetType().Name} activated.");

        try
        {
            _activationHookEntered = true;
            await OnActivatedAsync(cancellationToken);
        }
        catch
        {
            LifeCycleState = previousState;
            WriteTrace($"{GetType().Name} failed during activation.");
            throw;
        }
    }

    internal void DeactivateCore()
    {
        if (LifeCycleState != MicroLifeCycleState.Active)
            return;

        try
        {
            OnDeactivatedAsync().GetAwaiter().GetResult();
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

    internal async ValueTask DeactivateCoreAsync(CancellationToken cancellationToken = default)
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

    internal void UninitializeCore()
    {
        if (LifeCycleState != MicroLifeCycleState.Initialized)
            return;

        try
        {
            OnUninitializedAsync().GetAwaiter().GetResult();
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

    internal async ValueTask UninitializeCoreAsync(CancellationToken cancellationToken = default)
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

    internal void DetachCore()
        => DetachCore(skipRollback: false);

    internal void DetachCore(bool skipRollback)
    {
        if (Host is null)
            return;

        List<Exception> errors = [];

        if (!skipRollback && LifeCycleState == MicroLifeCycleState.Active)
            RollbackToInitialized(errors);

        if (!skipRollback && LifeCycleState == MicroLifeCycleState.Initialized)
            RollbackToAttached(errors);

        try
        {
            OnDetachedAsync().GetAwaiter().GetResult();
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

    internal void RollbackToCore(MicroLifeCycleState state)
    {
        List<Exception> errors = [];

        switch (state)
        {
            case MicroLifeCycleState.Detached:
                if (Host is not null)
                {
                    try
                    {
                        DetachCore();
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
                break;
            case MicroLifeCycleState.Attached:
                if (LifeCycleState == MicroLifeCycleState.Active)
                    RollbackToInitialized(errors);
                if (LifeCycleState == MicroLifeCycleState.Initialized)
                    RollbackToAttached(errors);
                break;
            case MicroLifeCycleState.Initialized:
                if (LifeCycleState == MicroLifeCycleState.Active)
                    RollbackToInitialized(errors);
                if (LifeCycleState == MicroLifeCycleState.Attached)
                {
                    try
                    {
                        InitializeCore();
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
                        ActivateCore();
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
                    Dispose();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
                break;
        }

        ThrowIfNeeded(errors);
    }

    internal ValueTask TickCoreAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
    {
        if (LifeCycleState != MicroLifeCycleState.Active)
            return ValueTask.CompletedTask;

        return OnTickAsync(deltaTime, cancellationToken);
    }

    private async ValueTask StartNodeAsyncCore(CancellationToken cancellationToken)
    {
        await InitializeCoreAsync(cancellationToken);
        await ActivateCoreAsync(cancellationToken);
    }

    private async ValueTask StopNodeAsyncCore(CancellationToken cancellationToken)
    {
        await DeactivateCoreAsync(cancellationToken);
    }

    public virtual void Dispose()
    {
        DisposeLifeCycle();
    }

    internal void DisposeLifeCycle()
        => DisposeLifeCycle(skipDetachRollback: false);

    internal void DisposeLifeCycle(bool skipDetachRollback)
    {
        if (LifeCycleState == MicroLifeCycleState.Disposed)
            return;

        List<Exception> errors = [];

        if (Host is not null)
        {
            try
            {
                DetachCore(skipDetachRollback);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        try
        {
            OnDisposedAsync().GetAwaiter().GetResult();
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

    protected THost GetRequiredHost()
        => Host ?? throw new InvalidOperationException("This lifecycle node is not attached to a host.");

    protected void ResetActivationHookTracking()
        => _activationHookEntered = false;

    protected virtual void OnAttached() { }

    protected virtual ValueTask OnAttachedAsync(CancellationToken cancellationToken = default)
    {
        OnAttached();
        return ValueTask.CompletedTask;
    }

    protected virtual void OnInitialized() { }

    protected virtual ValueTask OnInitializedAsync(CancellationToken cancellationToken = default)
    {
        OnInitialized();
        return ValueTask.CompletedTask;
    }

    protected virtual void OnActivated() { }

    protected virtual ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        OnActivated();
        return ValueTask.CompletedTask;
    }

    protected virtual ValueTask OnTickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    protected virtual void OnDeactivated() { }

    protected virtual ValueTask OnDeactivatedAsync(CancellationToken cancellationToken = default)
    {
        OnDeactivated();
        return ValueTask.CompletedTask;
    }

    protected virtual void OnUninitialized() { }

    protected virtual ValueTask OnUninitializedAsync(CancellationToken cancellationToken = default)
    {
        OnUninitialized();
        return ValueTask.CompletedTask;
    }

    protected virtual void OnDetached() { }

    protected virtual ValueTask OnDetachedAsync(CancellationToken cancellationToken = default)
    {
        OnDetached();
        return ValueTask.CompletedTask;
    }

    protected virtual void OnDisposed() { }

    protected virtual ValueTask OnDisposedAsync(CancellationToken cancellationToken = default)
    {
        OnDisposed();
        return ValueTask.CompletedTask;
    }

    private void EnsureAttached()
    {
        if (Host is null)
            throw new InvalidOperationException("This lifecycle node must be attached to a host before it can be initialized.");
    }

    private void EnsureNotDisposed()
    {
        if (LifeCycleState == MicroLifeCycleState.Disposed)
            throw new ObjectDisposedException(GetType().Name);
    }

    private void RollbackToInitialized(List<Exception> errors)
    {
        try
        {
            OnDeactivatedAsync().GetAwaiter().GetResult();
            LifeCycleState = MicroLifeCycleState.Initialized;
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private void RollbackToAttached(List<Exception> errors)
    {
        try
        {
            OnUninitializedAsync().GetAwaiter().GetResult();
            LifeCycleState = MicroLifeCycleState.Attached;
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
    }

    private static void ThrowIfNeeded(IReadOnlyList<Exception> errors)
    {
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();

        if (errors.Count > 1)
            throw new AggregateException(errors);
    }

    private static void WriteTrace(string message)
    {
        System.Diagnostics.Trace.WriteLine($"[MicroLifeCycle] {message}");
    }
}