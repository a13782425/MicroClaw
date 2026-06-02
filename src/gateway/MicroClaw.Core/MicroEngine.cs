using MicroClaw.Core.Logging;

namespace MicroClaw.Core;

/// <summary>引擎生命周期状态。</summary>
public enum MicroEngineState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    /// <summary>启动或停止过程中出现异常，需手动恢复。</summary>
    Faulted,
}

/// <summary>
/// 核心运行时：管理 <see cref="MicroObject"/>、<see cref="MicroService"/> 与并发 Tick 调度。
/// 每个可 tick 节点在调度器里有独立执行任务（obj 为独立执行单元，互不阻塞）；
/// 结构性变更经引擎锁串行化。同时提供轻量 service locator（<see cref="GetService{T}"/>）。
/// </summary>
public sealed class MicroEngine : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly MicroEvent _events = new();
    private readonly HashSet<MicroObject> _objects = [];
    private readonly HashSet<MicroService> _services = [];
    private readonly Dictionary<Type, object> _singletons = new();
    private readonly Dictionary<MicroLifecycle, MicroObjectRunner> _runners = new(ReferenceEqualityComparer.Instance);
    private readonly MicroTickSchedulerRunner _tickScheduler;
    private IMicroLogger? _logger;
    private int _disposed;

    /// <summary>当前引擎实例（用于组件触发注册等场景）。</summary>
    public static MicroEngine Instance { get; private set; } = null!;

    /// <summary>当前引擎 logger，惰性初始化以兼容启动期替换工厂。</summary>
    public IMicroLogger Logger => LazyInitializer.EnsureInitialized(ref _logger, () => MicroLogger.Factory.CreateLogger(GetType()));

    /// <summary>初始化引擎。</summary>
    public MicroEngine(IMicroLoggerFactory factory)
    {
        MicroLogger.Factory = factory;
        Logger?.LogDebug("MicroEngine start");
        Instance = this;
        _tickScheduler = new MicroTickSchedulerRunner(this);
    }

    /// <summary>当前引擎状态。</summary>
    public MicroEngineState State { get; private set; } = MicroEngineState.Stopped;

    /// <summary>引擎是否处于 Running 状态。</summary>
    public bool IsStarted => State == MicroEngineState.Running;

    /// <summary>从已注册服务/单例中查找，不存在时返回 null。</summary>
    public T? GetService<T>() where T : class
    {
        lock (_gate)
        {
            if (_singletons.TryGetValue(typeof(T), out object? singleton))
                return (T)singleton;

            T? service = _services.OfType<T>().FirstOrDefault();
            if (service is not null)
                return service;
        }
        return null;
    }

    /// <summary>从已注册服务/单例中查找，不存在时抛出。</summary>
    public T GetRequiredService<T>() where T : class => GetService<T>() ?? throw new InvalidOperationException($"Service '{typeof(T).FullName}' is not registered.");

    /// <summary>订阅引擎级事件。</summary>
    public IDisposable Subscribe<TEvent>(Func<TEvent, CancellationToken, ValueTask> handler) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(handler);
        ThrowIfDisposed();

        IDisposable subscription = _events.Subscribe(handler);

        if (Volatile.Read(ref _disposed) == 1)
        {
            subscription.Dispose();
            ThrowIfDisposed();
        }

        return subscription;
    }

    /// <summary>发布引擎级事件。</summary>
    public ValueTask PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ThrowIfDisposed();
        return _events.PublishAsync(domainEvent, cancellationToken);
    }

    /// <summary>当前已注册对象快照。</summary>
    public IReadOnlyList<MicroObject> Objects
    {
        get
        {
            lock (_gate)
            {
                return _objects.ToArray();
            }
        }
    }

    /// <summary>当前已注册服务的有序快照。</summary>
    public IReadOnlyList<MicroService> Services
    {
        get
        {
            lock (_gate)
            {
                return _services.OrderBy(static service => service.Order).ToArray();
            }
        }
    }

    /// <summary>启动引擎：标记 Running 并启动后台 Tick 循环。</summary>
    public ValueTask StartEngine(CancellationToken cancellationToken = default)
    {
        try
        {
            if (State == MicroEngineState.Running)
                return ValueTask.CompletedTask;

            if (State != MicroEngineState.Stopped)
                throw new InvalidOperationException($"MicroEngine cannot start while it is '{State}'.");

            State = MicroEngineState.Starting;
            WriteTrace("Engine starting.");
            State = MicroEngineState.Running;

            _ = _tickScheduler.RunAsync(cancellationToken);
            WriteTrace("Engine started.");
            return ValueTask.CompletedTask;
        }
        catch
        {
            MarkFaulted();
            throw;
        }
    }

    /// <summary>停止引擎：drain 所有 Tickable，再逆序停用对象与服务。错误统一收集后抛出。</summary>
    public async ValueTask StopEngine(CancellationToken cancellationToken = default)
    {
        MicroService[] serviceSnapshot;
        MicroObject[] objectSnapshot;
        List<Exception> errors = [];
        try
        {
            lock (_gate)
            {
                if (State == MicroEngineState.Stopped)
                    return;

                if (State is not (MicroEngineState.Running or MicroEngineState.Faulted))
                    throw new InvalidOperationException($"MicroEngine cannot stop while it is '{State}'.");

                State = MicroEngineState.Stopping;
                WriteTrace("Engine stopping.");
                serviceSnapshot = _services.OrderBy(static service => service.Order).ToArray();
                objectSnapshot = _objects.ToArray();
            }

            try
            {
                await _tickScheduler.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }

            // 逆序停用对象（停用即触发组件逆序停用）。
            foreach (MicroObject microObject in objectSnapshot.Reverse())
            {
                try
                {
                    await microObject.DisableCoreAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }

            // 逆序停用服务（停用即触发 StopAsync）。
            foreach (MicroService service in serviceSnapshot.Reverse())
            {
                try
                {
                    await service.DisableCoreAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
        }
        finally
        {
            lock (_gate)
            {
                State = errors.Count == 0 ? MicroEngineState.Stopped : MicroEngineState.Faulted;
            }

            WriteTrace(errors.Count == 0 ? "Engine stopped." : $"Engine stop completed with {errors.Count} error(s).");
        }

        ThrowIfNeeded(errors);
    }

    public bool RegisterSingleton<T>(T instance) where T : class => RegisterSingleton(typeof(T), instance);

    public bool RemoveSingleton<T>() where T : class
    {
        Type serviceType = typeof(T);
        if (!_singletons.TryGetValue(serviceType, out object? existing))
            return false;
        return RemoveSingleton(serviceType, existing);
    }

    public bool RemoveSingleton<T>(object instance) where T : class => RemoveSingleton(typeof(T), instance);

    public bool RemoveSingleton(object instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ThrowIfDisposed();
        lock (_gate)
        {
            foreach (Type key in _singletons.Where(pair => ReferenceEquals(pair.Value, instance)).Select(pair => pair.Key).ToArray())
            {
                _singletons.Remove(key);
            }
            return true;
        }
    }

    public bool RemoveSingleton(Type serviceType, object instance)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(instance);
        ThrowIfDisposed();
        lock (_gate)
        {
            if (!_singletons.TryGetValue(serviceType, out object? existing))
                return false;

            if (!ReferenceEquals(existing, instance))
                return false;

            _singletons.Remove(serviceType);
            return true;
        }
    }

    public bool MapSingleton<TService, TImplementation>() where TService : class where TImplementation : class, TService
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            if (!_singletons.TryGetValue(typeof(TImplementation), out object? instance))
                throw new InvalidOperationException($"Service '{typeof(TImplementation).FullName}' is not registered.");
            return RegisterSingleton(typeof(TService), instance);
        }
    }

    public async ValueTask<bool> RegisterAsync(MicroLifecycle lifecycle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        ThrowIfEngineNotRunning();

        if (lifecycle is MicroObject microObject)
            return await RegisterObjectAsync(microObject, cancellationToken);

        if (lifecycle is MicroService service)
            return await RegisterServiceAsync(service, cancellationToken);

        throw new ArgumentException("Unsupported lifecycle type.", nameof(lifecycle));
    }

    public async ValueTask<bool> UnregisterAsync(MicroLifecycle lifecycle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        if (lifecycle is MicroObject microObject)
            return await UnregisterObjectAsync(microObject, cancellationToken);
        if (lifecycle is MicroService service)
            return await UnregisterServiceAsync(service, cancellationToken);
        throw new ArgumentException("Unsupported lifecycle type.", nameof(lifecycle));
    }

    /// <summary>向引擎注册对象：驱动 OnAwake→OnStart 到 Active，并把可 tick 单元纳入调度。</summary>
    private async ValueTask<bool> RegisterObjectAsync(MicroObject microObject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(microObject);
        ThrowIfEngineNotRunning();

        lock (_gate)
        {
            if (_objects.Contains(microObject))
                return false;

            if (microObject.IsDestroyed)
                throw new ObjectDisposedException(nameof(MicroObject));

            microObject.Engine = this;
            _objects.Add(microObject);
        }

        try
        {
            await microObject.AwakeCoreAsync(cancellationToken);
            await microObject.StartCoreAsync(cancellationToken);
            RegisterRunner(microObject);
            WriteTrace($"Registered object {microObject.GetType().Name}.");
            return true;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _objects.Remove(microObject);
            }

            try
            {
                await microObject.DestroyFromEngineAsync(CancellationToken.None);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(ex, cleanup);
            }
            finally
            {
                microObject.Engine = null;
            }

            throw;
        }
    }

    /// <summary>从引擎注销对象（等价于 <see cref="MicroObject.Destroy(MicroObject, CancellationToken)"/>）。</summary>
    private async ValueTask<bool> UnregisterObjectAsync(MicroObject microObject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(microObject);

        lock (_gate)
        {
            ThrowIfEngineMutating();
            if (!_objects.Contains(microObject))
                return false;
        }

        await DestroyObjectAsync(microObject, cancellationToken);
        return true;
    }

    /// <summary>动态注册服务：驱动到 Running，并把可 tick 服务纳入调度；失败回滚。</summary>
    private async ValueTask<bool> RegisterServiceAsync(MicroService service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ThrowIfEngineNotRunning();

        lock (_gate)
        {
            if (_services.Contains(service))
                return false;

            if (service.IsDestroyed)
                throw new ObjectDisposedException(nameof(MicroService));

            service.Engine = this;
            _services.Add(service);
            RegisterSingleton(service.GetType(), service);
        }

        try
        {
            await service.AwakeCoreAsync(cancellationToken);
            await service.StartCoreAsync(cancellationToken);

            if (service is IMicroTickable tickable && service.IsActive)
                EnsureTickable(tickable, service.Order, service.GetType().Name);

            WriteTrace($"Registered service {service.GetType().Name}.");
            return true;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _services.Remove(service);
                RemoveSingleton(service.GetType(), service);
            }

            try
            {
                await service.DestroyFromEngineAsync(CancellationToken.None);
            }
            catch (Exception cleanup)
            {
                throw new AggregateException(ex, cleanup);
            }
            finally
            {
                service.Engine = null;
            }

            throw;
        }
    }

    /// <summary>从引擎注销服务（等价于销毁）。</summary>
    private async ValueTask<bool> UnregisterServiceAsync(MicroService service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);

        lock (_gate)
        {
            ThrowIfEngineMutating();
            if (!_services.Contains(service))
                return false;
        }

        await DestroyServiceAsync(service, cancellationToken);
        return true;
    }

    /// <summary>在引擎上下文内销毁一个已注册对象：drain 其 tick，执行 OnDestroy，移出注册表。</summary>
    internal async ValueTask DestroyObjectAsync(MicroObject microObject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(microObject);

        if (Volatile.Read(ref _disposed) == 1)
        {
            await microObject.DestroyFromEngineAsync(cancellationToken);
            microObject.Engine = null;
            return;
        }

        bool wasRegistered;
        lock (_gate)
        {
            wasRegistered = _objects.Contains(microObject);
        }

        List<Exception> errors = [];

        if (wasRegistered)
        {
            MicroObjectRunner? runner;
            lock (_gate)
            {
                _runners.Remove(microObject, out runner);
            }

            await DrainTickableAsync(runner, errors);
        }

        try
        {
            await microObject.DestroyFromEngineAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            FlattenInto(errors, ex);
        }

        if (wasRegistered)
        {
            lock (_gate)
            {
                _objects.Remove(microObject);
            }
        }

        microObject.Engine = null;
        ThrowIfNeeded(errors);
    }

    /// <summary>在引擎上下文内销毁一个已注册服务。</summary>
    internal async ValueTask DestroyServiceAsync(MicroService service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);

        if (Volatile.Read(ref _disposed) == 1)
        {
            await service.DestroyFromEngineAsync(cancellationToken);
            service.Engine = null;
            return;
        }

        bool wasRegistered;
        lock (_gate)
        {
            wasRegistered = _services.Contains(service);
        }

        List<Exception> errors = [];

        if (wasRegistered)
            await DrainTickableAsync(service as IMicroTickable, errors);

        try
        {
            await service.DestroyFromEngineAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            FlattenInto(errors, ex);
        }

        if (wasRegistered)
        {
            lock (_gate)
            {
                if (_services.Remove(service))
                    RemoveSingleton(service);
            }
        }

        service.Engine = null;
        ThrowIfNeeded(errors);
    }

    /// <summary>把运行期新增的组件入队到其 obj 的执行单元，下一帧安全点 bring-online（双缓冲）。</summary>
    internal ValueTask ScheduleComponentAddAsync(MicroObject owner, MicroComponent component, CancellationToken cancellationToken = default)
    {
        MicroObjectRunner? runner;
        lock (_gate)
        {
            _runners.TryGetValue(owner, out runner);
        }

        if (runner is not null)
            runner.EnqueueAdd(component);

        return ValueTask.CompletedTask;
    }

    /// <summary>把组件入队到其 obj 的执行单元，下一帧安全点摘除并销毁（双缓冲）；无 runner 时本地直接处理。</summary>
    internal ValueTask ScheduleComponentRemoveAsync(MicroObject owner, MicroComponent component, CancellationToken cancellationToken = default)
    {
        MicroObjectRunner? runner;
        lock (_gate)
        {
            _runners.TryGetValue(owner, out runner);
        }

        if (runner is not null)
        {
            runner.EnqueueRemove(component);
            return ValueTask.CompletedTask;
        }

        return owner.DetachComponentCoreAsync(component, cancellationToken);
    }

    private bool RegisterSingleton(Type serviceType, object instance)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentNullException.ThrowIfNull(instance);
        ThrowIfDisposed();

        lock (_gate)
        {
            if (_singletons.TryGetValue(serviceType, out object? existing))
            {
                if (ReferenceEquals(existing, instance))
                    return false;

                throw new InvalidOperationException($"Singleton '{serviceType.FullName}' is already registered.");
            }

            _singletons.Add(serviceType, instance);
            return true;
        }
    }

    /// <summary>为对象创建并注册其执行单元 runner（obj 内串行 tick + 双缓冲安全点）。</summary>
    private void RegisterRunner(MicroObject microObject)
    {
        MicroObjectRunner runner = new(this, microObject);
        lock (_gate)
        {
            _runners[microObject] = runner;
        }

        _tickScheduler.Register(runner, 0, microObject.GetType().Name, clearIsolation: true);
    }

    /// <summary>注册一个可 tick 节点（若尚未注册）。</summary>
    private void EnsureTickable(IMicroTickable tickable, int order, string displayName)
    {
        if (_tickScheduler.IsRegistered(tickable))
            return;

        _tickScheduler.Register(tickable, order, displayName, clearIsolation: true);
    }

    /// <summary>drain 一个可 tick 节点并从调度移除；异常收集。</summary>
    private async ValueTask DrainTickableAsync(IMicroTickable? tickable, List<Exception> errors, CancellationToken cancellationToken = default)
    {
        if (tickable is null)
            return;

        try
        {
            await _tickScheduler.DrainAndRemoveAsync(tickable, cancellationToken);
        }
        catch (Exception ex)
        {
            FlattenInto(errors, ex);
        }
    }

    /// <summary>在引擎处于启动/停止阶段时阻止结构变更。</summary>
    private void ThrowIfEngineMutating()
    {
        if (State is MicroEngineState.Starting or MicroEngineState.Stopping)
            throw new InvalidOperationException($"MicroEngine cannot be mutated while it is '{State}'.");
    }

    private void ThrowIfEngineNotRunning()
    {
        if (State != MicroEngineState.Running)
            throw new InvalidOperationException("引擎不在运行，无法执行此操作。");
    }

    /// <summary>将异常扁平化后追加到目标列表，避免嵌套 <see cref="AggregateException"/>。</summary>
    internal static void FlattenInto(List<Exception> target, Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.Flatten().InnerExceptions)
                target.Add(inner);
        }
        else
        {
            target.Add(exception);
        }
    }

    /// <summary>根据收集到的异常数量统一抛出。</summary>
    private static void ThrowIfNeeded(List<Exception> errors)
    {
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();

        if (errors.Count > 1)
            throw new AggregateException(errors);
    }

    /// <summary>
    /// 每个已注册 <see cref="MicroObject"/> 的执行单元：在调度器里作为单个 tickable 运行（obj 间并发）。
    /// 每帧帧首是“安全点”：先应用双缓冲的结构变更（新增组件 bring-online、移除组件 detach+destroy），
    /// 再按顺序串行 tick 该 obj 的可 tick 组件（obj 内串行）以及 obj 自身。
    /// </summary>
    private sealed class MicroObjectRunner(MicroEngine engine, MicroObject microObject) : IMicroTickable
    {
        private readonly MicroEngine _engine = engine;
        private readonly MicroObject _obj = microObject;
        private readonly Lock _gate = new();
        private readonly List<MicroComponent> _pendingAdd = [];
        private readonly List<MicroComponent> _pendingRemove = [];

        private readonly List<MicroComponent> _toAdd = [];
        private readonly List<MicroComponent> _toRemove = [];

        /// <summary>tick 节律：obj 自身可 tick 则用其 Frame，否则用默认 10。该 obj 下组件统一以此节律串行 tick。</summary>
        public uint Frame => _obj is IMicroTickable tickable ? tickable.Frame : 10u;

        /// <summary>入队一个待 bring-online 的新组件（运行期挂载）。</summary>
        public void EnqueueAdd(MicroComponent component)
        {
            lock (_gate)
            {
                _pendingAdd.Add(component);
            }
        }

        /// <summary>入队一个待摘除并销毁的组件（运行期卸载）。</summary>
        public void EnqueueRemove(MicroComponent component)
        {
            lock (_gate)
            {
                _pendingRemove.Add(component);
            }
        }

        public async ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
        {
            // 安全点：应用本帧累积的结构变更（双缓冲），不在下面的 tick 遍历途中改动集合。
            lock (_gate)
            {
                _toAdd.Clear();
                _toRemove.Clear();
                _toAdd.AddRange(_pendingAdd);
                _pendingAdd.Clear();
                _toRemove.AddRange(_pendingAdd);
                _pendingRemove.Clear();
            }

            foreach (MicroComponent component in _toAdd)
                await _obj.AttachComponentCoreAsync(component, cancellationToken);

            foreach (MicroComponent component in _toRemove)
                await _obj.DetachComponentCoreAsync(component, cancellationToken);

            // 层级 Enabled 闸门：obj 停用或未激活则本帧不 tick 任何组件。
            if (!_obj.Enabled || !_obj.IsActive)
                return;

            // obj 内串行：组件在本执行单元里顺序 tick；单个组件异常被隔离，不影响同 obj 其余组件。
            foreach (MicroComponent component in _obj.Components)
            {
                if (component is not IMicroTickable componentTickable || !component.Enabled)
                    continue;

                try
                {
                    await componentTickable.TickAsync(deltaTime, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _engine.WriteTrace($"Component tick {component.GetType().Name} failed: {ex.Message}");
                }
            }

            if (_obj is IMicroTickable objectTickable)
            {
                try
                {
                    await objectTickable.TickAsync(deltaTime, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    _engine.WriteTrace($"Object tick {_obj.GetType().Name} failed: {ex.Message}");
                }
            }
        }

        public override int GetHashCode()
        {
            return _obj.GetHashCode();
        }
    }

    /// <summary>引擎内部的 Tick 调度与后台循环执行器。</summary>
    private sealed class MicroTickSchedulerRunner
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(4);

        private readonly MicroEngine _owner;
        private readonly Lock _gate = new();
        private readonly Dictionary<IMicroTickable, TickableRegistration> _registrations = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<IMicroTickable> _isolatedTickables = new(ReferenceEqualityComparer.Instance);
        private Task? _runLoopTask;
        private CancellationTokenSource? _runLoopSignal;
        private bool _acceptFrames = true;

        public MicroTickSchedulerRunner(MicroEngine owner)
        {
            _owner = owner;
        }

        /// <summary>指定节点是否已注册。</summary>
        public bool IsRegistered(IMicroTickable tickable)
        {
            lock (_gate)
            {
                return _registrations.ContainsKey(tickable);
            }
        }

        /// <summary>注册或更新一个可调度节点。</summary>
        public void Register(IMicroTickable tickable, int order, string displayName, bool clearIsolation)
        {
            lock (_gate)
            {
                if (!clearIsolation && _isolatedTickables.Contains(tickable))
                    return;

                if (clearIsolation)
                    _isolatedTickables.Remove(tickable);

                if (_registrations.TryGetValue(tickable, out TickableRegistration? existing))
                    throw new InvalidOperationException($"Tickable '{displayName}' is already registered with order {existing.Order}.");

                _registrations[tickable] = new TickableRegistration(tickable, order, displayName);
            }
        }

        /// <summary>等待指定节点排空并将其移除。</summary>
        public async ValueTask DrainAndRemoveAsync(IMicroTickable tickable, CancellationToken cancellationToken)
        {
            TickableRegistration? registration;
            Task waitTask;

            lock (_gate)
            {
                if (!_registrations.TryGetValue(tickable, out registration))
                    return;

                registration.AcceptsFrames = false;
                registration.PendingDelta = TimeSpan.Zero;
                waitTask = registration.GetDrainTask();
            }

            try
            {
                await waitTask.WaitAsync(cancellationToken);
            }
            catch
            {
                lock (_gate)
                {
                    if (_registrations.TryGetValue(tickable, out TickableRegistration? existing) && ReferenceEquals(existing, registration))
                        registration.AcceptsFrames = true;
                }

                throw;
            }

            lock (_gate)
            {
                if (_registrations.TryGetValue(tickable, out TickableRegistration? existing) && ReferenceEquals(existing, registration))
                    _registrations.Remove(tickable);
            }
        }

        /// <summary>启动后台 Tick 循环。</summary>
        public Task RunAsync(CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_runLoopTask is { IsCompleted: false })
                    throw new InvalidOperationException("MicroEngine tick loop is already running.");

                _acceptFrames = true;
                CancellationTokenSource runLoopSignal = new();
                _runLoopSignal = runLoopSignal;
                _runLoopTask = RunLoopCoreAsync(runLoopSignal, cancellationToken);
                return _runLoopTask;
            }
        }

        /// <summary>停止后台 Tick 循环并排空所有已注册节点。</summary>
        public async ValueTask StopAsync(CancellationToken cancellationToken)
        {
            Task? runLoopTask;
            CancellationTokenSource? runLoopSignal;
            TickableRegistration[] drainingRegistrations;
            Task[] drainTasks;

            lock (_gate)
            {
                _acceptFrames = false;
                runLoopTask = _runLoopTask;
                runLoopSignal = _runLoopSignal;

                foreach (TickableRegistration registration in _registrations.Values)
                {
                    registration.AcceptsFrames = false;
                    registration.PendingDelta = TimeSpan.Zero;
                }

                drainingRegistrations = _registrations.Values.ToArray();
                drainTasks = drainingRegistrations.Select(static registration => registration.GetDrainTask()).ToArray();
            }

            if (runLoopSignal is not null)
            {
                try
                {
                    runLoopSignal.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }

            if (runLoopTask is not null)
            {
                try
                {
                    await runLoopTask.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }
                catch (Exception)
                {
                }
            }

            if (drainTasks.Length > 0)
            {
                await Task.WhenAll(drainTasks).WaitAsync(cancellationToken);
            }

            lock (_gate)
            {
                foreach (TickableRegistration registration in drainingRegistrations)
                {
                    if (_registrations.TryGetValue(registration.Tickable, out TickableRegistration? existing) && ReferenceEquals(existing, registration))
                        _registrations.Remove(registration.Tickable);
                }

                if (_runLoopTask?.IsCompleted != false)
                {
                    _runLoopTask = null;
                    _runLoopSignal?.Dispose();
                    _runLoopSignal = null;
                }
            }
        }

        /// <summary>后台循环的核心执行逻辑。</summary>
        private async Task RunLoopCoreAsync(CancellationTokenSource runLoopSignal, CancellationToken cancellationToken)
        {
            using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runLoopSignal.Token);
            CancellationToken linkedToken = linkedCts.Token;

            try
            {
                using PeriodicTimer timer = new(TickInterval);
                DateTimeOffset previousTick = DateTimeOffset.UtcNow;

                while (!linkedToken.IsCancellationRequested)
                {
                    try
                    {
                        if (!await timer.WaitForNextTickAsync(linkedToken))
                            break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    DateTimeOffset now = DateTimeOffset.UtcNow;
                    TimeSpan deltaTime = now - previousTick;
                    previousTick = now;

                    EnqueueFrame(deltaTime, linkedToken);
                }
            }
            finally
            {
                lock (_gate)
                {
                    if (ReferenceEquals(_runLoopSignal, runLoopSignal))
                    {
                        runLoopSignal.Dispose();
                        _runLoopSignal = null;
                    }
                }
            }
        }

        /// <summary>将一帧时间分发到当前活动注册项。</summary>
        private void EnqueueFrame(TimeSpan deltaTime, CancellationToken dispatchCancellationToken)
        {
            lock (_gate)
            {
                if (!_acceptFrames)
                    return;

                TickableRegistration[] registrations = _registrations.Values.Where(static registration => registration.AcceptsFrames).OrderBy(static registration => registration.Order).ToArray();

                foreach (TickableRegistration registration in registrations)
                {
                    registration.PendingDelta += deltaTime;
                    EnsureExecutionLocked(registration, dispatchCancellationToken);
                }
            }
        }

        /// <summary>确保指定注册项已经拥有正在运行的执行任务。</summary>
        private void EnsureExecutionLocked(TickableRegistration registration, CancellationToken dispatchCancellationToken)
        {
            if (registration.IsExecuting || !registration.AcceptsFrames || registration.PendingDelta <= registration.TickInterval)
                return;

            registration.IsExecuting = true;
            registration.EnsureBusy();
            registration.ExecutionTask = Task.Run(() => ExecuteTickableAsync(registration, dispatchCancellationToken), CancellationToken.None);
        }

        /// <summary>执行单个注册项的实际 Tick 循环。</summary>
        private async Task ExecuteTickableAsync(TickableRegistration registration, CancellationToken dispatchCancellationToken)
        {
            while (true)
            {
                TimeSpan deltaTime;

                lock (_gate)
                {
                    if (!registration.AcceptsFrames || registration.PendingDelta <= TimeSpan.Zero)
                    {
                        registration.PendingDelta = TimeSpan.Zero;
                        registration.IsExecuting = false;
                        registration.ExecutionTask = null;
                        registration.MarkIdle();
                        return;
                    }

                    deltaTime = registration.PendingDelta;
                    registration.PendingDelta = TimeSpan.Zero;
                }

                try
                {
                    await registration.Tickable.TickAsync(deltaTime, dispatchCancellationToken);
                }
                catch (OperationCanceledException) when (dispatchCancellationToken.IsCancellationRequested)
                {
                    lock (_gate)
                    {
                        registration.PendingDelta = TimeSpan.Zero;
                        registration.IsExecuting = false;
                        registration.ExecutionTask = null;
                        registration.MarkIdle();
                    }

                    return;
                }
                catch (Exception ex)
                {
                    lock (_gate)
                    {
                        registration.LastException = ex;
                        registration.AcceptsFrames = false;
                        registration.PendingDelta = TimeSpan.Zero;
                        registration.IsExecuting = false;
                        registration.ExecutionTask = null;
                        _isolatedTickables.Add(registration.Tickable);
                        registration.MarkIdle();
                    }

                    _owner.WriteTrace($"Tickable {registration.DisplayName} failed: {ex.Message}");
                    return;
                }
            }
        }

        /// <summary>释放后台循环关联的非托管句柄；调用前应已通过 <see cref="StopAsync"/> 停止。</summary>
        public void Dispose()
        {
            CancellationTokenSource? signal;
            lock (_gate)
            {
                _acceptFrames = false;
                signal = _runLoopSignal;
                _runLoopSignal = null;
                _runLoopTask = null;
                _registrations.Clear();
                _isolatedTickables.Clear();
            }

            if (signal is null)
                return;

            try
            {
                signal.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            signal.Dispose();
        }
    }

    /// <summary>调度器中单个 Tickable 的运行状态。</summary>
    private sealed class TickableRegistration
    {
        private TaskCompletionSource<bool> _idleSignal = CreateCompletedSignal();

        public TickableRegistration(IMicroTickable tickable, int order, string displayName)
        {
            Tickable = tickable;
            Order = order;
            DisplayName = displayName;
            TickInterval = TimeSpan.FromSeconds(1.0 / Math.Clamp(tickable.Frame, 1u, 120u));
        }

        public IMicroTickable Tickable { get; }
        public int Order { get; set; }
        public string DisplayName { get; set; }
        public bool AcceptsFrames { get; set; } = true;
        public bool IsExecuting { get; set; }
        public TimeSpan TickInterval { get; set; }
        public TimeSpan PendingDelta { get; set; }
        public Exception? LastException { get; set; }
        public Task? ExecutionTask { get; set; }

        public void EnsureBusy()
        {
            if (_idleSignal.Task.IsCompleted)
                _idleSignal = CreatePendingSignal();
        }

        public Task GetDrainTask()
        {
            if (!IsExecuting && PendingDelta <= TimeSpan.Zero)
                return Task.CompletedTask;

            EnsureBusy();
            return _idleSignal.Task;
        }

        public void MarkIdle() => _idleSignal.TrySetResult(true);

        private static TaskCompletionSource<bool> CreateCompletedSignal()
        {
            TaskCompletionSource<bool> signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            signal.TrySetResult(true);
            return signal;
        }

        private static TaskCompletionSource<bool> CreatePendingSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>异步释放引擎：先尽力而为 StopEngine（吞异常并记 trace），幂等。</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        bool needsStop;
        lock (_gate)
        {
            needsStop = State is MicroEngineState.Running or MicroEngineState.Faulted;
        }

        if (needsStop)
        {
            try
            {
                await StopEngine(CancellationToken.None);
            }
            catch (Exception ex)
            {
                WriteTrace($"Engine dispose: StopAsync threw and was swallowed: {ex.GetType().Name}: {ex.Message}");
            }
        }

        _events.Clear();

        try
        {
            _tickScheduler.Dispose();
        }
        catch (Exception ex)
        {
            WriteTrace($"Engine dispose: tick scheduler dispose threw and was swallowed: {ex.GetType().Name}: {ex.Message}");
        }

        WriteTrace("Engine disposed.");
    }

    /// <summary>将引擎状态标记为 Faulted。</summary>
    internal void MarkFaulted()
    {
        lock (_gate)
        {
            if (State == MicroEngineState.Running)
                State = MicroEngineState.Faulted;
        }

        WriteTrace("Engine marked faulted.");
    }

    private void WriteTrace(string message) => Logger.LogDebug(message);

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) == 1)
            throw new ObjectDisposedException(nameof(MicroEngine));
    }
}
