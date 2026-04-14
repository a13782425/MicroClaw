namespace MicroClaw.Core;

public enum MicroComponentState
{
    Detached,
    Attached,
    Initialized,
    Active,
}

public abstract class MicroComponent
{
    public MicroObject? Host { get; private set; }

    public MicroComponentState State { get; private set; } = MicroComponentState.Detached;

    public bool IsAttached => Host is not null;

    public bool IsActive => State == MicroComponentState.Active;

    public TComponent? GetComponent<TComponent>() where TComponent : MicroComponent
        => Host?.GetComponent<TComponent>();

    public TComponent AddComponent<TComponent>(TComponent component) where TComponent : MicroComponent
        => GetRequiredHost().AddComponent(component);

    public TComponent AddComponent<TComponent>() where TComponent : MicroComponent, new()
        => GetRequiredHost().AddComponent<TComponent>();

    public bool RemoveComponent<TComponent>() where TComponent : MicroComponent
        => GetRequiredHost().RemoveComponent<TComponent>();

    public bool RemoveComponent(MicroComponent component)
        => GetRequiredHost().RemoveComponent(component);

    internal void AttachTo(MicroObject host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (Host is not null && !ReferenceEquals(Host, host))
            throw new InvalidOperationException("A component can only belong to one MicroObject at a time.");

        if (ReferenceEquals(Host, host))
            return;

        Host = host;
        State = MicroComponentState.Attached;
        try
        {
            OnAttached();
        }
        catch
        {
            Host = null;
            State = MicroComponentState.Detached;
            throw;
        }
    }

    internal void Initialize()
    {
        if (State == MicroComponentState.Initialized || State == MicroComponentState.Active)
            return;

        EnsureAttached();
        MicroComponentState previousState = State;
        State = MicroComponentState.Initialized;
        try
        {
            OnInitialized();
        }
        catch
        {
            State = previousState;
            throw;
        }
    }

    internal void Activate()
    {
        if (State == MicroComponentState.Active)
            return;

        Initialize();
        MicroComponentState previousState = State;
        State = MicroComponentState.Active;
        try
        {
            OnActivated();
        }
        catch
        {
            State = previousState;
            throw;
        }
    }

    internal void Deactivate()
    {
        if (State != MicroComponentState.Active)
            return;

        try
        {
            OnDeactivated();
            State = MicroComponentState.Initialized;
        }
        catch
        {
            State = MicroComponentState.Active;
            throw;
        }
    }

    internal void Uninitialize()
    {
        if (State != MicroComponentState.Initialized)
            return;

        try
        {
            OnUninitialized();
            State = MicroComponentState.Attached;
        }
        catch
        {
            State = MicroComponentState.Initialized;
            throw;
        }
    }

    internal void DetachFromHost()
    {
        if (Host is null)
            return;

        List<Exception> errors = [];

        if (State == MicroComponentState.Active)
            RollbackToInitialized(errors);

        if (State == MicroComponentState.Initialized)
            RollbackToAttached(errors);

        try
        {
            OnDetached();
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
        finally
        {
            Host = null;
            State = MicroComponentState.Detached;
        }

        ThrowIfNeeded(errors);
    }

    internal void RollbackTo(MicroComponentState state)
    {
        List<Exception> errors = [];

        switch (state)
        {
            case MicroComponentState.Detached:
                if (Host is not null)
                {
                    try
                    {
                        DetachFromHost();
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
                break;
            case MicroComponentState.Attached:
                if (State == MicroComponentState.Active)
                    RollbackToInitialized(errors);
                if (State == MicroComponentState.Initialized)
                    RollbackToAttached(errors);
                break;
            case MicroComponentState.Initialized:
                if (State == MicroComponentState.Active)
                    RollbackToInitialized(errors);
                if (State == MicroComponentState.Attached)
                {
                    try
                    {
                        Initialize();
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
                break;
            case MicroComponentState.Active:
                if (State != MicroComponentState.Active)
                {
                    try
                    {
                        Activate();
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
                break;
        }

        ThrowIfNeeded(errors);
    }

    protected MicroObject GetRequiredHost()
        => Host ?? throw new InvalidOperationException("This component is not attached to a MicroObject.");

    protected virtual void OnAttached()
    {
    }

    protected virtual void OnInitialized()
    {
    }

    protected virtual void OnActivated()
    {
    }

    protected virtual void OnDeactivated()
    {
    }

    protected virtual void OnUninitialized()
    {
    }

    protected virtual void OnDetached()
    {
    }

    private void RollbackToInitialized(List<Exception> errors)
    {
        try
        {
            OnDeactivated();
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
        finally
        {
            State = MicroComponentState.Initialized;
        }
    }

    private void RollbackToAttached(List<Exception> errors)
    {
        try
        {
            OnUninitialized();
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }
        finally
        {
            State = MicroComponentState.Attached;
        }
    }

    private void EnsureAttached()
    {
        if (Host is null)
            throw new InvalidOperationException("This component must be attached to a MicroObject before it can be initialized.");
    }

    private static void ThrowIfNeeded(IReadOnlyList<Exception> errors)
    {
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();

        if (errors.Count > 1)
            throw new AggregateException(errors);
    }
}