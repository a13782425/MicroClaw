namespace MicroClaw.Core;

/// <summary>对象生命周期状态。</summary>
public enum MicroObjectState
{
    Created,
    Initialized,
    Active,
    Disposed,
}

/// <summary>
/// 引擎中的实体对象，采用组件模式（Component Pattern）。
/// 通过 <see cref="AddComponent{TComponent}()"/> 挂载功能组件，生命周期随引擎同步变更。
/// </summary>
public class MicroObject : MicroLifeCycle<MicroEngine>
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Type, MicroComponent> _components = new();
    private bool _isTransitioning;

    /// <summary>所属引擎，未注册时为 null。</summary>
    public MicroEngine? Engine => Host;

    /// <summary>当前对象状态。</summary>
    public MicroObjectState State => MapState(LifeCycleState);

    public IReadOnlyList<MicroComponent> Components
    {
        get
        {
            lock (_gate)
            {
                return _components.Values.ToArray();
            }
        }
    }

    /// <summary>创建并挂载指定类型的组件（使用无参构造函数）。</summary>
    public TComponent AddComponent<TComponent>() where TComponent : MicroComponent, new()
        => AddComponent(new TComponent());

    /// <summary>挂载已有组件实例；若对象已激活则同步初始化并激活该组件。</summary>
    public TComponent AddComponent<TComponent>(TComponent component) where TComponent : MicroComponent
    {
        ArgumentNullException.ThrowIfNull(component);

        Type componentType = component.GetType();
        bool shouldInitialize;
        bool shouldActivate;

        lock (_gate)
        {
            ThrowIfDisposed();
            ThrowIfTransitioning();

            _isTransitioning = true;

            if (component.Host is not null && !ReferenceEquals(component.Host, this))
            {
                _isTransitioning = false;
                throw new InvalidOperationException("A component can only belong to one MicroObject at a time.");
            }

            if (_components.ContainsKey(componentType))
            {
                _isTransitioning = false;
                throw new InvalidOperationException($"Component type '{componentType.Name}' is already attached to this MicroObject.");
            }

            _components.Add(componentType, component);
            shouldInitialize = LifeCycleState is MicroLifeCycleState.Initialized or MicroLifeCycleState.Active;
            shouldActivate = LifeCycleState == MicroLifeCycleState.Active;
        }

        try
        {
            component.AttachTo(this);

            if (shouldInitialize)
                component.Initialize();

            if (shouldActivate)
                component.Activate();

            return component;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _components.Remove(componentType);
            }

            List<Exception> rollbackErrors = [];
            if (ReferenceEquals(component.Host, this))
                CollectRollbackError(component, MicroComponentState.Detached, rollbackErrors);

            if (rollbackErrors.Count == 0)
                throw;

            rollbackErrors.Insert(0, ex);
            throw new AggregateException(rollbackErrors);
        }
        finally
        {
            lock (_gate)
            {
                _isTransitioning = false;
            }
        }
    }

    /// <summary>获取指定类型的组件，不存在时返回 null；支持按基类/接口查找（存在歧义时抛出异常）。</summary>
    public TComponent? GetComponent<TComponent>() where TComponent : MicroComponent
        => TryGetComponent<TComponent>(out TComponent? component) ? component : null;

    public bool TryGetComponent<TComponent>(out TComponent? component) where TComponent : MicroComponent
    {
        lock (_gate)
        {
            if (!TryResolveComponent(typeof(TComponent), out MicroComponent? resolved))
            {
                component = null;
                return false;
            }

            if (resolved is null)
            {
                component = null;
                return false;
            }

            component = (TComponent)resolved;
            return true;
        }
    }

    public bool RemoveComponent<TComponent>() where TComponent : MicroComponent
    {
        MicroComponent? component;
        Type? componentType;

        lock (_gate)
        {
            ThrowIfTransitioning();

            _isTransitioning = true;

            try
            {
                if (!TryResolveComponent(typeof(TComponent), out component))
                {
                    _isTransitioning = false;
                    return false;
                }

                if (component is null)
                {
                    _isTransitioning = false;
                    return false;
                }

                componentType = component.GetType();
                _components.Remove(componentType);
            }
            catch
            {
                _isTransitioning = false;
                throw;
            }
        }

        try
        {
            component.DetachFromHost();
            return true;
        }
        catch
        {
            if (ReferenceEquals(component.Host, this))
            {
                lock (_gate)
                {
                    _components[componentType!] = component;
                }
            }

            throw;
        }
        finally
        {
            lock (_gate)
            {
                _isTransitioning = false;
            }
        }
    }

    public bool RemoveComponent(MicroComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);

        bool removed;
        lock (_gate)
        {
            ThrowIfTransitioning();

            _isTransitioning = true;

            removed = _components.TryGetValue(component.GetType(), out MicroComponent? existing)
                && ReferenceEquals(existing, component)
                && _components.Remove(component.GetType());
        }

        if (!removed)
        {
            lock (_gate)
            {
                _isTransitioning = false;
            }
            return false;
        }

        try
        {
            component.DetachFromHost();
            return true;
        }
        catch
        {
            if (ReferenceEquals(component.Host, this))
            {
                lock (_gate)
                {
                    _components[component.GetType()] = component;
                }
            }

            throw;
        }
        finally
        {
            lock (_gate)
            {
                _isTransitioning = false;
            }
        }
    }

    /// <summary>激活对象及其所有组件；对象必须先挂载到引擎。</summary>
    public void Activate()
    {
        MicroLifeCycleState previousState;

        lock (_gate)
        {
            ThrowIfDisposed();

            if (LifeCycleState == MicroLifeCycleState.Active)
                return;

            ThrowIfTransitioning();
            _isTransitioning = true;
            previousState = LifeCycleState;
        }

        try
        {
            ActivateCore();
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                try
                {
                    RollbackToCore(previousState);
                }
                catch (Exception rollbackException)
                {
                    AlignLifeCycleStateToComponents(previousState);
                    throw CreateAggregate(ex, rollbackException);
                }
            }

            throw;
        }
        finally
        {
            lock (_gate)
            {
                _isTransitioning = false;
            }
        }
    }

    /// <summary>停用对象及其所有组件（按逆序停用），状态回退到 Initialized；未激活则跳过。</summary>
    public void Deactivate()
    {
        lock (_gate)
        {
            if (LifeCycleState != MicroLifeCycleState.Active)
                return;

            ThrowIfTransitioning();
            _isTransitioning = true;
        }

        try
        {
            DeactivateCore();
        }
        finally
        {
            lock (_gate)
            {
                _isTransitioning = false;
            }
        }
    }

    /// <summary>释放对象；若已注册到引擎则委托引擎执行销毁，否则直接调用 <see cref="DisposeCore"/>。</summary>
    public override void Dispose()
    {
        if (Engine is { } engine)
        {
            engine.DisposeObject(this);
            return;
        }

        DisposeCore();
    }

    internal void DisposeCore()
    {
        lock (_gate)
        {
            if (IsDisposed)
                return;

            ThrowIfTransitioning();
            _isTransitioning = true;
        }

        try
        {
            DisposeLifeCycle();
        }
        finally
        {
            lock (_gate)
            {
                _isTransitioning = false;
            }
        }
    }

    internal void AttachToEngine(MicroEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        if (Host is not null && !ReferenceEquals(Host, engine))
            throw new InvalidOperationException("A MicroObject can only belong to one MicroEngine at a time.");

        AttachToHost(engine);
    }

    internal void DetachFromEngine(MicroEngine engine)
    {
        if (ReferenceEquals(Host, engine))
            DetachCore();
    }

    internal void RollbackToState(MicroObjectState state)
        => RollbackToCore(state switch
        {
            MicroObjectState.Created => Host is null ? MicroLifeCycleState.Detached : MicroLifeCycleState.Attached,
            MicroObjectState.Initialized => MicroLifeCycleState.Initialized,
            MicroObjectState.Active => MicroLifeCycleState.Active,
            MicroObjectState.Disposed => MicroLifeCycleState.Disposed,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        });

    protected override ValueTask OnInitializedAsync(CancellationToken cancellationToken = default)
    {
        ExecuteComponentTransition(
            snapshotFactory: static snapshot => snapshot,
            transition: static component => component.Initialize(),
            rollback: static (component, previousState, errors) => CollectRollbackError(component, previousState, errors),
            ownsTransitionGuard: !_isTransitioning);

        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        ExecuteComponentTransition(
            snapshotFactory: static snapshot => snapshot,
            transition: static component => component.Activate(),
            rollback: static (component, previousState, errors) => CollectRollbackError(component, previousState, errors),
            ownsTransitionGuard: !_isTransitioning);

        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnTickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
    {
        MicroComponent[] snapshot;

        lock (_gate)
        {
            snapshot = _components.Values.ToArray();
        }

        foreach (MicroComponent component in snapshot)
            await component.TickNodeAsync(deltaTime, cancellationToken);
    }

    protected override ValueTask OnDeactivatedAsync(CancellationToken cancellationToken = default)
    {
        ExecuteComponentTransition(
            snapshotFactory: static snapshot => snapshot.Reverse().ToArray(),
            transition: static component => component.Deactivate(),
            rollback: static (component, previousState, errors) => CollectRollbackError(component, previousState, errors),
            ownsTransitionGuard: !_isTransitioning,
            preserveCompletedTransitionsOnFailure: true);

        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnUninitializedAsync(CancellationToken cancellationToken = default)
    {
        ExecuteComponentTransition(
            snapshotFactory: static snapshot => snapshot.Reverse().ToArray(),
            transition: static component => component.RollbackTo(MicroComponentState.Attached),
            rollback: static (component, previousState, errors) => CollectRollbackError(component, previousState, errors),
            ownsTransitionGuard: !_isTransitioning,
            preserveCompletedTransitionsOnFailure: true);

        return ValueTask.CompletedTask;
    }

    protected override ValueTask OnDisposedAsync(CancellationToken cancellationToken = default)
    {
        MicroComponent[] snapshot;

        lock (_gate)
        {
            snapshot = _components.Values.Reverse().ToArray();
        }

        List<Exception> errors = [];

        foreach (MicroComponent component in snapshot)
        {
            try
            {
                component.DetachFromHost();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }

            try
            {
                component.DisposeLifeCycle();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
            finally
            {
                lock (_gate)
                {
                    _components.Remove(component.GetType());
                }
            }
        }

        ThrowIfNeeded(errors);
        return ValueTask.CompletedTask;
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(MicroObject));
    }

    private void AlignLifeCycleStateToComponents(MicroLifeCycleState fallbackState)
    {
        lock (_gate)
        {
            if (_components.Values.Any(static component => component.State == MicroComponentState.Active))
            {
                SetLifeCycleState(MicroLifeCycleState.Active);
                return;
            }

            if (_components.Values.Any(static component => component.State == MicroComponentState.Initialized))
            {
                SetLifeCycleState(MicroLifeCycleState.Initialized);
                return;
            }

            SetLifeCycleState(Host is null ? MicroLifeCycleState.Detached : fallbackState);
        }
    }

    private void ThrowIfTransitioning()
    {
        if (_isTransitioning)
            throw new InvalidOperationException("MicroObject cannot be mutated while a lifecycle transition is in progress.");
    }

    private bool TryResolveComponent(Type requestedType, out MicroComponent? component)
    {
        if (_components.TryGetValue(requestedType, out component))
            return true;

        MicroComponent? match = null;
        foreach (MicroComponent candidate in _components.Values)
        {
            if (!requestedType.IsAssignableFrom(candidate.GetType()))
                continue;

            if (match is not null)
                throw new InvalidOperationException($"Multiple components are assignable to type '{requestedType.Name}'.");

            match = candidate;
        }

        component = match;
        return component is not null;
    }

    private void ExecuteComponentTransition(
        Func<MicroComponent[], MicroComponent[]> snapshotFactory,
        Action<MicroComponent> transition,
        Action<MicroComponent, MicroComponentState, List<Exception>> rollback,
        bool ownsTransitionGuard,
        bool preserveCompletedTransitionsOnFailure = false)
    {
        MicroComponent[] snapshot;

        lock (_gate)
        {
            if (ownsTransitionGuard)
            {
                ThrowIfTransitioning();
                _isTransitioning = true;
            }

            snapshot = snapshotFactory(_components.Values.ToArray());
        }

        List<(MicroComponent Component, MicroComponentState PreviousState)> transitioned = [];

        try
        {
            foreach (MicroComponent component in snapshot)
            {
                MicroComponentState previousState = component.State;

                try
                {
                    transition(component);
                    transitioned.Add((component, previousState));
                }
                catch (Exception ex)
                {
                    if (preserveCompletedTransitionsOnFailure)
                        throw;

                    List<Exception> rollbackErrors = [];
                    rollback(component, previousState, rollbackErrors);

                    foreach ((MicroComponent transitionedComponent, MicroComponentState componentState) in transitioned.AsEnumerable().Reverse())
                        rollback(transitionedComponent, componentState, rollbackErrors);

                    if (rollbackErrors.Count == 0)
                        throw;

                    rollbackErrors.Insert(0, ex);
                    throw new AggregateException(rollbackErrors);
                }
            }
        }
        finally
        {
            if (ownsTransitionGuard)
            {
                lock (_gate)
                {
                    _isTransitioning = false;
                }
            }
        }
    }

    private static void CollectRollbackError(MicroComponent component, MicroComponentState targetState, List<Exception> rollbackErrors)
    {
        try
        {
            component.RollbackTo(targetState);
        }
        catch (Exception ex)
        {
            rollbackErrors.Add(ex);
        }
    }

    private static MicroObjectState MapState(MicroLifeCycleState state)
        => state switch
        {
            MicroLifeCycleState.Detached => MicroObjectState.Created,
            MicroLifeCycleState.Attached => MicroObjectState.Created,
            MicroLifeCycleState.Initialized => MicroObjectState.Initialized,
            MicroLifeCycleState.Active => MicroObjectState.Active,
            MicroLifeCycleState.Disposed => MicroObjectState.Disposed,
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

    private static void ThrowIfNeeded(IReadOnlyList<Exception> errors)
    {
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();

        if (errors.Count > 1)
            throw new AggregateException(errors);
    }

    private static AggregateException CreateAggregate(Exception primaryException, Exception rollbackException)
    {
        List<Exception> errors = [];

        if (primaryException is AggregateException primaryAggregate)
            errors.AddRange(primaryAggregate.InnerExceptions);
        else
            errors.Add(primaryException);

        if (rollbackException is AggregateException rollbackAggregate)
            errors.AddRange(rollbackAggregate.InnerExceptions);
        else
            errors.Add(rollbackException);

        return new AggregateException(errors);
    }
}