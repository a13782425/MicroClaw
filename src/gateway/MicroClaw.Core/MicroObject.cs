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
public class MicroObject
{
    private readonly Lock _gate = new();                             // 保护组件字典及状态字段的互斥锁
    private readonly Dictionary<Type, MicroComponent> _components = new();
    private bool _isTransitioning;                                     // 标记正在进行生命周期转换，防止并发修改

    /// <summary>所属引擎，未注册时为 null。</summary>
    public MicroEngine? Engine { get; private set; }

    /// <summary>当前对象状态。</summary>
    public MicroObjectState State { get; private set; } = MicroObjectState.Created;

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
            shouldInitialize = State is MicroObjectState.Initialized or MicroObjectState.Active;
            shouldActivate = State == MicroObjectState.Active;
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

    /// <summary>激活对象及其所有组件（先初始化未初始化的组件，再逐一激活）；已激活则跳过。</summary>
    public void Activate()
    {
        MicroComponent[] snapshot;
        MicroObjectState previousState;

        lock (_gate)
        {
            ThrowIfDisposed();
            ThrowIfTransitioning();

            if (State == MicroObjectState.Active)
                return;

            _isTransitioning = true;
            previousState = State;
            State = MicroObjectState.Active;
            snapshot = _components.Values.ToArray();
        }

        List<(MicroComponent Component, MicroComponentState PreviousState)> activated = [];

        try
        {
            foreach (MicroComponent component in snapshot)
            {
                MicroComponentState previousComponentState = component.State;

                try
                {
                    component.Initialize();
                    component.Activate();
                    activated.Add((component, previousComponentState));
                }
                catch (Exception ex)
                {
                    List<Exception> rollbackErrors = [];
                    CollectRollbackError(component, previousComponentState, rollbackErrors);

                    foreach ((MicroComponent activatedComponent, MicroComponentState componentState) in activated.AsEnumerable().Reverse())
                        CollectRollbackError(activatedComponent, componentState, rollbackErrors);

                    lock (_gate)
                    {
                        if (State != MicroObjectState.Disposed)
                            State = previousState;
                    }

                    if (rollbackErrors.Count == 0)
                        throw;

                    rollbackErrors.Insert(0, ex);
                    throw new AggregateException(rollbackErrors);
                }
            }
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
        MicroComponent[] snapshot;
        MicroObjectState previousState;

        lock (_gate)
        {
            if (State != MicroObjectState.Active)
                return;

            ThrowIfTransitioning();

            _isTransitioning = true;
            previousState = State;
            State = MicroObjectState.Initialized;
            snapshot = _components.Values.Reverse().ToArray();
        }

        List<(MicroComponent Component, MicroComponentState PreviousState)> deactivated = [];

        try
        {
            foreach (MicroComponent component in snapshot)
            {
                MicroComponentState previousComponentState = component.State;

                try
                {
                    component.Deactivate();
                    deactivated.Add((component, previousComponentState));
                }
                catch (Exception ex)
                {
                    List<Exception> rollbackErrors = [];
                    CollectRollbackError(component, previousComponentState, rollbackErrors);

                    foreach ((MicroComponent deactivatedComponent, MicroComponentState componentState) in deactivated.AsEnumerable().Reverse())
                        CollectRollbackError(deactivatedComponent, componentState, rollbackErrors);

                    lock (_gate)
                    {
                        if (State != MicroObjectState.Disposed)
                            State = previousState;
                    }

                    if (rollbackErrors.Count == 0)
                        throw;

                    rollbackErrors.Insert(0, ex);
                    throw new AggregateException(rollbackErrors);
                }
            }
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
    public void Dispose()
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
        MicroComponent[] snapshot;

        lock (_gate)
        {
            if (State == MicroObjectState.Disposed)
                return;

            ThrowIfTransitioning();

            _isTransitioning = true;
            snapshot = _components.Values.Reverse().ToArray();
            State = MicroObjectState.Disposed;
        }

        List<Exception> errors = [];

        try
        {
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
                finally
                {
                    lock (_gate)
                    {
                        _components.Remove(component.GetType());
                    }
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                _isTransitioning = false;
            }
        }

        ThrowIfNeeded(errors);
    }

    internal void AttachToEngine(MicroEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        if (Engine is not null && !ReferenceEquals(Engine, engine))
            throw new InvalidOperationException("A MicroObject can only belong to one MicroEngine at a time.");

        Engine = engine;
    }

    internal void DetachFromEngine(MicroEngine engine)
    {
        if (ReferenceEquals(Engine, engine))
            Engine = null;
    }

    private void ThrowIfDisposed()
    {
        if (State == MicroObjectState.Disposed)
            throw new ObjectDisposedException(nameof(MicroObject));
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

    private static void ThrowIfNeeded(IReadOnlyList<Exception> errors)
    {
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();

        if (errors.Count > 1)
            throw new AggregateException(errors);
    }
}