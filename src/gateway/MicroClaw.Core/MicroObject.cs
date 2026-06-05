namespace MicroClaw.Core;

/// <summary>
/// 引擎中的实体对象：组件容器 + 生命周期转发器 + 独立执行单元（采用组件模式）。
/// <para>
/// 自身不写业务逻辑，把两段生命周期（OnAwake→OnStart）与停用/销毁转发给其 components；
/// 一致性边界为单个 obj：先全员 OnAwake、再全员 OnStart。
/// </para>
/// <para>
/// 组件触发注册：首个 component 挂载时，该 obj 接入 <see cref="MicroEngine.Instance"/>；
/// obj 仅在 <see cref="Destroy(MicroObject, CancellationToken)"/> 时离开引擎。
/// </para>
/// </summary>
public class MicroObject : MicroLifecycle
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Type, MicroComponent> _components = new();

    /// <summary>所属引擎；未接入时为 null。由 <see cref="MicroEngine"/> 在注册/销毁时维护。</summary>
    public MicroEngine? Engine { get; internal set; }

    /// <summary>当前已挂载组件的快照。</summary>
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

    public static async ValueTask<T> Create<T>() where T : MicroObject, new()
    {
        T obj = new T();
        await MicroEngine.Instance.RegisterAsync(obj);
        return obj;
    }

    /// <summary>销毁一个对象（级联反序销毁其全部组件并离开引擎）。类比 Unity <c>Object.Destroy</c>。</summary>
    public static ValueTask Destroy(MicroObject microObject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(microObject);
        return microObject.DestroyCoreAsync(cancellationToken);
    }

    /// <summary>销毁一个组件（从其 obj 与引擎调度移除）。类比 Unity <c>Object.Destroy</c>。</summary>
    public static ValueTask Destroy(MicroComponent component, CancellationToken cancellationToken = default) => MicroComponent.Destroy(component, cancellationToken);

    /// <summary>创建并挂载指定类型组件（无参构造）。</summary>
    public async ValueTask<TComponent> AddComponentAsync<TComponent>(CancellationToken cancellationToken = default) where TComponent : MicroComponent, new()
    {
        TComponent component = new TComponent();
        Type type = typeof(TComponent);

        lock (_gate)
        {
            if (IsDestroyed)
                throw new ObjectDisposedException(nameof(MicroObject));
            if (component.Owner is not null && !ReferenceEquals(component.Owner, this))
                throw new InvalidOperationException("A component can only belong to one MicroObject at a time.");
            if (!_components.TryAdd(type, component))
                throw new InvalidOperationException($"Component type '{type.Name}' is already attached to this MicroObject.");
            component.Owner = this;
        }

        // 组件触发注册：首个组件令本 obj 接入引擎（若尚未接入且存在引擎实例）。
        // 注册时会同步把 obj 及其当前全部组件 bring-online（经 *CoreAsync 转发），含本组件。
        if (Engine is null && MicroEngine.Instance is { } instance)
        {
            await instance.RegisterAsync(this, cancellationToken);
            return component;
        }

        // obj 已在引擎上：把新组件入队，由该 obj 的执行单元在下一帧安全点 bring-online
        // （双缓冲：OnStart 不在本帧执行，避免在 tick 遍历途中改动集合）。
        if (Engine is { } host)
            await host.ScheduleComponentAddAsync(this, component, cancellationToken);
        else
            // 脱离引擎：无 tick 循环可冲突，本地直接 bring-online。
            await AttachComponentCoreAsync(component, cancellationToken);

        return component;
    }
    /// <summary>把单个组件推进到与 obj 当前生命周期一致的状态（各驱动幂等）。</summary>
    internal async ValueTask AttachComponentCoreAsync(MicroComponent component, CancellationToken cancellationToken)
    {
        switch (this.LifeCycleState)
        {
            case MicroLifeCycleState.PendingStart:
                await component.AwakeCoreAsync(cancellationToken);
                break;
            case MicroLifeCycleState.Active:
                await component.AwakeCoreAsync(cancellationToken);
                await component.StartCoreAsync(cancellationToken);
                break;
            case MicroLifeCycleState.Disabled:
                await component.AwakeCoreAsync(cancellationToken);
                await component.StartCoreAsync(cancellationToken);
                await component.DisableCoreAsync(cancellationToken);
                break;
        }
    }

    /// <summary>获取指定类型的组件，不存在时返回 null；支持按基类/接口查找（歧义时抛出）。</summary>
    public TComponent? GetComponent<TComponent>() where TComponent : MicroComponent
        => TryGetComponent(out TComponent? component) ? component : null;

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

    /// <summary>移除并销毁匹配指定类型的组件。</summary>
    public async ValueTask<bool> RemoveComponentAsync<TComponent>(CancellationToken cancellationToken = default) where TComponent : MicroComponent
    {
        MicroComponent? component;
        lock (_gate)
        {
            if (!TryResolveComponent(typeof(TComponent), out component) || component is null)
                return false;
        }

        await DestroyComponentAsync(component, cancellationToken);
        return true;
    }

    /// <summary>移除并销毁当前对象上的指定组件实例。</summary>
    public async ValueTask<bool> RemoveComponentAsync(MicroComponent component, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);
        lock (_gate)
        {
            if (!_components.TryGetValue(component.GetType(), out MicroComponent? existing) || !ReferenceEquals(existing, component))
                return false;
        }

        await DestroyComponentAsync(component, cancellationToken);
        return true;
    }

    /// <summary>销毁指定组件：挂在引擎上时入队到本 obj 执行单元的安全点处理（双缓冲）；脱离引擎时本地直接处理。obj 本身保留。</summary>
    public ValueTask DestroyComponentAsync(MicroComponent component, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(component);

        bool isOurs;
        lock (_gate)
        {
            isOurs = _components.TryGetValue(component.GetType(), out MicroComponent? existing) && ReferenceEquals(existing, component);
        }

        if (!isOurs)
            return ValueTask.CompletedTask;

        return Engine is { } engine
            ? engine.ScheduleComponentRemoveAsync(this, component, cancellationToken)
            : DetachComponentCoreAsync(component, cancellationToken);
    }

    /// <summary>
    /// 实际把组件从本 obj 摘除并销毁（幂等）。由执行单元安全点或脱离引擎时调用。
    /// </summary>
    internal async ValueTask DetachComponentCoreAsync(MicroComponent component, CancellationToken cancellationToken = default)
    {
        bool removed;
        lock (_gate)
        {
            removed = _components.TryGetValue(component.GetType(), out MicroComponent? existing)
                      && ReferenceEquals(existing, component)
                      && _components.Remove(component.GetType());
        }

        if (!removed)
            return;

        await component.DestroyFromOwnerAsync(cancellationToken);
        component.Owner = null;
    }

    /// <summary>
    /// Object Core Awake：先执行对象自身 hook，再推进所有 component Awake。
    /// 子类 override OnAwakeAsync 不会吞掉 component Awake。
    /// </summary>
    internal override async ValueTask AwakeCoreAsync(CancellationToken cancellationToken = default)
    {
        if (LifeCycleState != MicroLifeCycleState.Created)
            return;

        await base.AwakeCoreAsync(cancellationToken);

        await ForEachComponentAsync(
            static (component, ct) => component.AwakeCoreAsync(ct),
            reverse: false,
            cancellationToken);
    }

    /// <summary>
    /// Object Core Start：先启动 components，再执行对象自身 OnStartAsync。
    /// 这样子类 OnStartAsync 中可以安全读取已 Start 的 components。
    /// </summary>
    internal override async ValueTask StartCoreAsync(CancellationToken cancellationToken = default)
    {
        if (LifeCycleState != MicroLifeCycleState.PendingStart)
            return;

        await ForEachComponentAsync(
            static (component, ct) => component.StartCoreAsync(ct),
            reverse: false,
            cancellationToken);

        await base.StartCoreAsync(cancellationToken);
    }

    /// <summary>
    /// Object Core Enable：先启用对象自身，再启用 components。
    /// </summary>
    internal override async ValueTask EnableCoreAsync(CancellationToken cancellationToken = default)
    {
        bool shouldEnableComponents = LifeCycleState == MicroLifeCycleState.Disabled;

        await base.EnableCoreAsync(cancellationToken);

        if (!shouldEnableComponents || LifeCycleState != MicroLifeCycleState.Active)
            return;

        await ForEachComponentAsync(
            static (component, ct) => component.EnableCoreAsync(ct),
            reverse: false,
            cancellationToken);
    }

    /// <summary>
    /// Object Core Disable：先逆序停用 components，再停用对象自身。
    /// </summary>
    internal override async ValueTask DisableCoreAsync(CancellationToken cancellationToken = default)
    {
        if (LifeCycleState != MicroLifeCycleState.Active)
        {
            await base.DisableCoreAsync(cancellationToken);
            return;
        }

        await ForEachComponentAsync(
            static (component, ct) => component.DisableCoreAsync(ct),
            reverse: true,
            cancellationToken);

        await base.DisableCoreAsync(cancellationToken);
    }

    /// <summary>
    /// 销毁请求入口：挂在 Engine 上时交给 Engine 协调；脱离 Engine 时直接本地销毁。
    /// </summary>
    internal override ValueTask DestroyCoreAsync(CancellationToken cancellationToken = default)
        => Engine is { } engine ? engine.DestroyObjectAsync(this) : DestroyFromEngineAsync(cancellationToken);
    /// <summary>
    /// Engine 已完成 drain / unregister 后调用的实际销毁入口。
    /// 不走 Engine，避免 DestroyCoreAsync -> Engine -> DestroyCoreAsync 递归。
    /// </summary>
    internal async ValueTask DestroyFromEngineAsync(CancellationToken cancellationToken = default)
    {
        List<Exception> errors = [];

        // Destroy 前先走正常 Disable 流程：
        // component reverse disable -> object OnDisable。
        if (LifeCycleState == MicroLifeCycleState.Active)
        {
            try
            {
                await DisableCoreAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        MicroComponent[] snapshot;
        lock (_gate)
        {
            snapshot = _components.Values.Reverse().ToArray();
            _components.Clear();
        }

        // component OnDestroy 反序，且不通过 Owner 再次路由。
        foreach (MicroComponent component in snapshot)
        {
            try
            {
                await component.DestroyFromOwnerAsync(cancellationToken);
                component.Owner = null;
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        // 最后销毁 object 自身，触发业务子类 override 的 OnDestroyAsync。
        try
        {
            await base.DestroyCoreAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }

        ThrowIfNeeded(errors);
    }
    /// <summary>对组件快照执行同一动作；收集异常后统一抛出。</summary>
    private async ValueTask ForEachComponentAsync(Func<MicroComponent, CancellationToken, ValueTask> action, bool reverse, CancellationToken cancellationToken)
    {
        MicroComponent[] snapshot;
        lock (_gate)
        {
            IEnumerable<MicroComponent> values = _components.Values;
            snapshot = (reverse ? values.Reverse() : values).ToArray();
        }

        List<Exception> errors = [];
        foreach (MicroComponent component in snapshot)
        {
            try
            {
                await action(component, cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        ThrowIfNeeded(errors);
    }

    /// <summary>查找一个可赋值到指定运行时类型的组件（歧义时抛出）。</summary>
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
}
