namespace MicroClaw.Core;
/// <summary>
/// 引擎中的实体对象，采用组件模式（Component Pattern）。
/// 通过 <see cref="AddComponentAsync{TComponent}(CancellationToken)"/> 挂载功能组件，生命周期随引擎同步变更。
/// </summary>
public class MicroObject : MicroLifeCycle<MicroEngine>
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Type, MicroComponent> _components = new();
    private bool _isTransitioning;

    /// <summary>所属引擎，未注册时为 null。</summary>
    public MicroEngine? Engine => Host;

    /// <summary>获取当前已挂载组件的快照。</summary>
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
    public ValueTask<TComponent> AddComponentAsync<TComponent>(CancellationToken cancellationToken = default) where TComponent : MicroComponent, new() => AddComponentAsync(new TComponent(), cancellationToken);

    /// <summary>挂载已有组件实例；若对象已激活则同步初始化并激活该组件。</summary>
    public async ValueTask<TComponent> AddComponentAsync<TComponent>(TComponent component, CancellationToken cancellationToken = default) where TComponent : MicroComponent
    {
        ArgumentNullException.ThrowIfNull(component);
        Type type = component.GetType();

        lock (_gate)
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(MicroObject));
            if (component.Host is not null && !ReferenceEquals(component.Host, this))
                throw new InvalidOperationException("A component can only belong to one MicroObject at a time.");
            if (_components.ContainsKey(type))
                throw new InvalidOperationException($"Component type '{type.Name}' is already attached to this MicroObject.");
            _components.Add(type, component);
        }

        // 只有对象处于激活态才让组件自动激活；否则先存“不激活”的意图。
        if (!IsActive)
            await component.SetActiveAsync(false, cancellationToken);

        await component.AttachToHostAsync(this, cancellationToken);   // Start + 按 Enabled 自动 Active
        return component;
    }

    /// <summary>获取指定类型的组件，不存在时返回 null；支持按基类/接口查找（存在歧义时抛出异常）。</summary>
    public TComponent? GetComponent<TComponent>() where TComponent : MicroComponent => TryGetComponent<TComponent>(out TComponent? component) ? component : null;

    /// <summary>尝试解析一个可赋值到指定类型的组件。</summary>
    public bool TryGetComponent<TComponent>(out TComponent? component) where TComponent : MicroComponent
    {
        lock (_gate)
        {
            if (TryResolveComponent(typeof(TComponent), out MicroComponent? resolved) && resolved is not null)
            {
                component = (TComponent)resolved;
                return true;
            }
        }
        component = null;
        return false;
    }

    /// <summary>移除匹配指定类型的组件。</summary>
    public async ValueTask<bool> RemoveComponentAsync<TComponent>(CancellationToken cancellationToken = default) where TComponent : MicroComponent
    {
        MicroComponent? component;
        lock (_gate)
        {
            if (!TryResolveComponent(typeof(TComponent), out component) || component is null)
                return false;
            _components.Remove(component.GetType());
        }
        await component.DisposeAsync();
        return true;
    }

    /// <summary>移除当前对象上的指定组件实例。</summary>
    public async ValueTask<bool> RemoveComponentAsync(MicroComponent component, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        lock (_gate)
        {
            if (!_components.TryGetValue(component.GetType(), out MicroComponent? existing) || !ReferenceEquals(existing, component))
                return false;
            _components.Remove(component.GetType());
        }
        await component.DisposeAsync();
        return true;
    }
    internal override async ValueTask DisposeCoreAsync(CancellationToken cancellationToken = default)
    {
        if (Engine is { } engine)
        {
            await engine.DisposeObjectAsync(this);
        }
        await base.DisposeCoreAsync(cancellationToken);
    }

    /// <summary>将对象挂接到指定引擎宿主。</summary>
    internal ValueTask AttachToEngineAsync(MicroEngine engine, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);

        if (Host is not null && !ReferenceEquals(Host, engine))
            throw new InvalidOperationException("A MicroObject can only belong to one MicroEngine at a time.");

        return AttachToHostAsync(engine, cancellationToken);
    }

    /// <summary>将对象从当前引擎宿主上分离。</summary>
    internal ValueTask DetachFromEngineAsync(MicroEngine engine, CancellationToken cancellationToken = default)
    {
        if (ReferenceEquals(Host, engine))
            return DetachCoreAsync(cancellationToken: cancellationToken);

        return ValueTask.CompletedTask;
    }

    /// <summary>将对象回滚到指定的生命周期状态。</summary>
    internal ValueTask RollbackToStateAsync(MicroLifeCycleState state, CancellationToken cancellationToken = default) => RollbackToCoreAsync(state, cancellationToken);

    /// <summary>初始化当前已挂载的全部组件。</summary>
    protected override async ValueTask OnInitializedAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteComponentTransitionAsync(snapshotFactory: static snapshot => snapshot, transition: static (component, ct) => component.InitializeAsync(ct), rollback: static (component, previousState, errors, ct) => CollectRollbackErrorAsync(component, previousState, errors, ct), ownsTransitionGuard: !_isTransitioning, cancellationToken: cancellationToken);
    }

    /// <summary>激活当前已挂载的全部组件。</summary>
    protected override async ValueTask OnActivatedAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteComponentTransitionAsync(snapshotFactory: static snapshot => snapshot, transition: static (component, ct) => component.ActivateAsync(ct), rollback: static (component, previousState, errors, ct) => CollectRollbackErrorAsync(component, previousState, errors, ct), ownsTransitionGuard: !_isTransitioning, cancellationToken: cancellationToken);
    }

    /// <summary>按挂载逆序停用全部组件。</summary>
    protected override async ValueTask OnDeactivatedAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteComponentTransitionAsync(snapshotFactory: static snapshot => snapshot.Reverse().ToArray(), transition: static (component, ct) => component.DeactivateAsync(ct), rollback: static (component, previousState, errors, ct) => CollectRollbackErrorAsync(component, previousState, errors, ct), ownsTransitionGuard: !_isTransitioning, preserveCompletedTransitionsOnFailure: true, cancellationToken: cancellationToken);
    }

    /// <summary>将已初始化组件回滚到已挂载状态。</summary>
    protected override async ValueTask OnUninitializedAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteComponentTransitionAsync(snapshotFactory: static snapshot => snapshot.Reverse().ToArray(), transition: static (component, ct) => component.RollbackToAsync(MicroLifeCycleState.Attached, ct), rollback: static (component, previousState, errors, ct) => CollectRollbackErrorAsync(component, previousState, errors, ct), ownsTransitionGuard: !_isTransitioning, preserveCompletedTransitionsOnFailure: true, cancellationToken: cancellationToken);
    }

    /// <summary>在对象释放阶段分离并释放全部已挂载组件。</summary>
    protected override async ValueTask OnDisposedAsync(CancellationToken cancellationToken = default)
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
                await component.DetachFromHostAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }

            try
            {
                await component.DisposeLifeCycleAsync(cancellationToken: cancellationToken);
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

        _events.Clear();

        ThrowIfNeeded(errors);
    }

    /// <summary>在生命周期变更进行中时抛出异常。</summary>
    private void ThrowIfTransitioning()
    {
        if (_isTransitioning)
            throw new InvalidOperationException("MicroObject cannot be mutated while a lifecycle transition is in progress.");
    }

    /// <summary>查找一个可赋值到指定运行时类型的组件。</summary>
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

    /// <summary>对组件快照执行同一类生命周期变更。</summary>
    private async ValueTask ExecuteComponentTransitionAsync(Func<MicroComponent[], MicroComponent[]> snapshotFactory, Func<MicroComponent, CancellationToken, ValueTask> transition, Func<MicroComponent, MicroLifeCycleState, List<Exception>, CancellationToken, ValueTask> rollback, bool ownsTransitionGuard, CancellationToken cancellationToken, bool preserveCompletedTransitionsOnFailure = false)
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

        List<(MicroComponent Component, MicroLifeCycleState PreviousState)> transitioned = [];

        try
        {
            foreach (MicroComponent component in snapshot)
            {
                MicroLifeCycleState previousState = component.LifeCycleState;

                try
                {
                    await transition(component, cancellationToken);
                    transitioned.Add((component, previousState));
                }
                catch (Exception ex)
                {
                    if (preserveCompletedTransitionsOnFailure)
                        throw;

                    List<Exception> rollbackErrors = [];
                    await rollback(component, previousState, rollbackErrors, CancellationToken.None);

                    foreach ((MicroComponent transitionedComponent, MicroLifeCycleState componentState) in transitioned.AsEnumerable().Reverse())
                        await rollback(transitionedComponent, componentState, rollbackErrors, CancellationToken.None);

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

    /// <summary>尝试回滚单个组件并记录失败。</summary>
    private static async ValueTask CollectRollbackErrorAsync(MicroComponent component, MicroLifeCycleState targetState, List<Exception> rollbackErrors, CancellationToken cancellationToken)
    {
        try
        {
            await component.RollbackToAsync(targetState, cancellationToken);
        }
        catch (Exception ex)
        {
            rollbackErrors.Add(ex);
        }
    }

    /// <summary>将主异常与回滚异常合并为一个扁平化聚合异常，避免嵌套的 <see cref="AggregateException"/>。</summary>
    private static AggregateException CreateAggregate(Exception primaryException, Exception rollbackException)
    {
        List<Exception> errors = [];

        MicroEngine.FlattenInto(errors, primaryException);
        MicroEngine.FlattenInto(errors, rollbackException);

        return new AggregateException(errors);
    }

}