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
/// 核心运行时，负责管理 <see cref="MicroObject"/>、<see cref="MicroService"/> 以及内部 Tick 调度。
/// 通过执行门（execution gate）保证 Start / Stop / 手动 Tick / Mutation 的线程安全。
/// </summary>
public sealed class MicroEngine : IAsyncDisposable
{
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly AsyncLocal<ExecutionScopeState?> _executionScope = new();
    private readonly AsyncLocal<ObjectLifecycleGateState?> _objectLifecycleGate = new();
    private readonly IServiceProvider _serviceProvider;
    private readonly List<MicroObject> _objects = [];
    private readonly List<MicroService> _services = [];
    private readonly MicroTickSchedulerRunner _tickScheduler;
    private IMicroLogger? _logger;
    
    /// <summary>
    /// 当前生命周期节点的 logger，分类名取自运行时类型。惰性初始化以便宿主在启动阶段
    /// 替换 <see cref="MicroLogger.Factory"/> 后仍能被后续实例拾取到。
    /// 使用 <see cref="LazyInitializer.EnsureInitialized{T}(ref T,Func{T})"/> 保证并发安全：
    /// 即使多个线程同时首次访问，最终也只有一个 <see cref="IMicroLogger"/> 实例胜出并被缓存。
    /// </summary>
    public IMicroLogger Logger => LazyInitializer.EnsureInitialized(ref _logger, () => MicroLogger.Factory.CreateLogger(GetType()));
    
    private int _disposed;
    
    /// <summary>
    /// 初始化引擎并批量挂载初始服务；若任意服务挂载失败则回滚已挂载的全部服务。
    /// 为避免在存在 <see cref="SynchronizationContext"/>（例如 WPF/WinForms）时
    /// 阻塞调用者线程导致死锁，内部将异步挂载工作 offload 到线程池后再同步等待。
    /// 如果调用方本身位于异步上下文中，建议优先使用
    /// <see cref="CreateAsync(IServiceProvider, IEnumerable{MicroService}, CancellationToken)"/>。
    /// </summary>
    public MicroEngine(IServiceProvider serviceProvider, IEnumerable<MicroService> services) : this(serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(services);
        
        RunSynchronously(() => InitializeServicesAsync(services, CancellationToken.None));
    }
    
    private MicroEngine(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _tickScheduler = new MicroTickSchedulerRunner(this);
    }
    
    /// <summary>
    /// 异步工厂：按指定初始服务列表创建并挂载引擎。推荐在异步代码路径中使用，可避免
    /// 构造函数内部的 sync-over-async 行为。
    /// </summary>
    public static async ValueTask<MicroEngine> CreateAsync(IServiceProvider serviceProvider, IEnumerable<MicroService> services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        
        MicroEngine engine = new(serviceProvider);
        await engine.InitializeServicesAsync(services, cancellationToken);
        return engine;
    }
    
    private async ValueTask InitializeServicesAsync(IEnumerable<MicroService> services, CancellationToken cancellationToken)
    {
        List<MicroService> attachedServices = [];
        
        try
        {
            foreach (MicroService service in services)
            {
                if (service is null)
                    throw new ArgumentException("Initial services cannot contain null.", nameof(services));
                
                if (_services.Contains(service))
                    continue;
                
                await service.AttachToEngineAsync(this, cancellationToken);
                _services.Add(service);
                attachedServices.Add(service);
            }
        }
        catch (Exception ex)
        {
            List<Exception> rollbackErrors = [];
            
            foreach (MicroService service in attachedServices.AsEnumerable().Reverse())
            {
                _services.Remove(service);
                
                try
                {
                    await service.DetachFromEngineAsync(this, CancellationToken.None);
                }
                catch (Exception detachException)
                {
                    rollbackErrors.Add(detachException);
                }
            }
            
            if (rollbackErrors.Count == 0)
                throw;
            
            rollbackErrors.Insert(0, ex);
            throw new AggregateException(rollbackErrors);
        }
    }
    
    /// <summary>
    /// 在线程池上运行异步工作并同步等待，避免调用线程的 <see cref="SynchronizationContext"/>
    /// 造成 sync-over-async 死锁。
    /// </summary>
    private static void RunSynchronously(Func<ValueTask> work)
    {
        Task.Run(async () => await work()).GetAwaiter().GetResult();
    }
    
    /// <summary>当前引擎状态。</summary>
    public MicroEngineState State { get; private set; } = MicroEngineState.Stopped;
    
    /// <summary>引擎是否处于 Running 状态。</summary>
    public bool IsStarted => State == MicroEngineState.Running;
    
    /// <summary>从 DI 容器获取指定类型的服务，不存在时返回 null。</summary>
    public T? GetService<T>() where T : class => _serviceProvider.GetService(typeof(T)) as T;
    
    /// <summary>从 DI 容器获取指定类型的服务，不存在时抛出异常。</summary>
    public T GetRequiredService<T>() where T : class => GetService<T>() ?? throw new InvalidOperationException($"Service '{typeof(T).FullName}' is not registered.");
    
    /// <summary>获取当前已注册对象的快照。</summary>
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
    
    /// <summary>获取当前已注册服务的有序快照。</summary>
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
    
    /// <summary>
    /// 启动引擎：按 Order 顺序启动所有服务，然后激活所有已注册对象。
    /// 若任意步骤失败，自动回滚并将状态置为 Stopped 或 Faulted。
    /// </summary>
    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfReentered();
        
        MicroService[] serviceSnapshot;
        MicroObject[] objectSnapshot;
        bool gateAcquired = false;
        
        await EnterExecutionScopeAsync(cancellationToken);
        EnterReentrancyScope();
        gateAcquired = true;
        
        try
        {
            lock (_gate)
            {
                if (State == MicroEngineState.Running)
                    return;
                
                if (State != MicroEngineState.Stopped)
                    throw new InvalidOperationException($"MicroEngine cannot start while it is '{State}'.");
                
                State = MicroEngineState.Starting;
                WriteTrace("Engine starting.");
                serviceSnapshot = _services.OrderBy(static service => service.Order).ToArray();
                objectSnapshot = _objects.ToArray();
            }
            
            List<MicroService> startedServices = [];
            List<(MicroObject Object, MicroLifeCycleState PreviousState)> activatedObjects = [];
            
            try
            {
                foreach (MicroService service in serviceSnapshot)
                {
                    startedServices.Add(service);
                    await service.StartNodeAsync(cancellationToken);
                }
                
                foreach (MicroObject microObject in objectSnapshot)
                {
                    lock (_gate)
                    {
                        if (!_objects.Contains(microObject) || microObject.LifeCycleState == MicroLifeCycleState.Disposed)
                            continue;
                    }
                    
                    MicroLifeCycleState previousState = microObject.LifeCycleState;
                    
                    try
                    {
                        await microObject.ActivateAsync(cancellationToken);
                        activatedObjects.Add((microObject, previousState));
                    }
                    catch
                    {
                        if (microObject.LifeCycleState != previousState)
                            activatedObjects.Add((microObject, previousState));
                        
                        throw;
                    }
                }
                
                lock (_gate)
                {
                    State = MicroEngineState.Running;
                }
                
                RebuildTickableRegistry();
                WriteTrace("Engine started.");
            }
            catch (Exception startException)
            {
                List<Exception> rollbackErrors = [];
                
                foreach ((MicroObject microObject, MicroLifeCycleState previousState) in activatedObjects.AsEnumerable().Reverse())
                {
                    try
                    {
                        await microObject.RollbackToStateAsync(previousState, CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        rollbackErrors.Add(ex);
                    }
                }
                
                foreach (MicroService service in startedServices.AsEnumerable().Reverse())
                {
                    try
                    {
                        await service.StopNodeAsync(CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        rollbackErrors.Add(ex);
                    }
                }
                
                lock (_gate)
                {
                    State = rollbackErrors.Count == 0 ? MicroEngineState.Stopped : MicroEngineState.Faulted;
                }
                
                WriteTrace($"Engine start failed. Rollback errors: {rollbackErrors.Count}.");
                
                if (rollbackErrors.Count == 0)
                    throw;
                
                rollbackErrors.Insert(0, startException);
                throw new AggregateException(rollbackErrors);
            }
        }
        finally
        {
            if (gateAcquired)
                ExitExecutionScope();
        }
    }
    
    /// <summary>
    /// 启动引擎内部 Tick 循环。
    /// 通常由宿主层在引擎启动后调用。
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfReentered();
        
        Task runLoopTask;
        
        await EnterExecutionScopeAsync(cancellationToken);
        EnterReentrancyScope();
        
        try
        {
            lock (_gate)
            {
                if (State != MicroEngineState.Running)
                    throw new InvalidOperationException("MicroEngine must be started before its tick loop can run.");
            }
            
            runLoopTask = _tickScheduler.RunAsync(cancellationToken);
        }
        finally
        {
            ExitExecutionScope();
        }
        
        await runLoopTask;
    }
    
    /// <summary>
    /// 停止引擎：先冻结并 drain 所有 Tickable，再按逆序停用对象与服务。
    /// 收集所有错误后统一抛出，保证所有清理步骤都有机会执行。
    /// </summary>
    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfReentered();
        
        MicroService[] serviceSnapshot;
        MicroObject[] objectSnapshot;
        bool gateAcquired = false;
        
        await EnterExecutionScopeAsync(cancellationToken);
        EnterReentrancyScope();
        gateAcquired = true;
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
            
            // 即便 tick 调度器停止失败，也要继续执行对象/服务的尽力而为停用，
            // 避免状态挂在 Faulted 且所有业务组件仍保留为 Active 的泄漏情况。
            foreach (MicroObject microObject in objectSnapshot.Reverse())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    errors.Add(new OperationCanceledException(cancellationToken));
                    break;
                }
                
                try
                {
                    await microObject.DeactivateAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            }
            
            foreach (MicroService service in serviceSnapshot.Reverse())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    errors.Add(new OperationCanceledException(cancellationToken));
                    break;
                }
                
                try
                {
                    await service.StopNodeAsync(cancellationToken);
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
            
            if (gateAcquired)
                ExitExecutionScope();
        }
        
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        
        if (errors.Count > 1)
            throw new AggregateException(errors);
    }
    
    /// <summary>手动驱动一帧更新；主要用于测试和显式控制场景。</summary>
    public async ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
    {
        if (deltaTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "Tick delta time must be non-negative.");
        
        ThrowIfReentered();
        
        await EnterExecutionScopeAsync(cancellationToken);
        EnterReentrancyScope();
        try
        {
            lock (_gate)
            {
                if (State != MicroEngineState.Running)
                    throw new InvalidOperationException("MicroEngine must be started before ticking.");
                
                if (_tickScheduler.IsRunLoopActive)
                    throw new InvalidOperationException("MicroEngine cannot be manually ticked while its background tick loop is running.");
            }
            
            await _tickScheduler.DispatchAndWaitAsync(deltaTime, cancellationToken);
        }
        finally
        {
            ExitExecutionScope();
        }
    }
    
    /// <summary>向引擎注册对象；若引擎已在运行则立即激活该对象。</summary>
    public async ValueTask<bool> RegisterObjectAsync(MicroObject microObject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(microObject);
        ThrowIfReentered();
        
        if (!await TryEnterExecutionScopeAsync(cancellationToken))
            throw new InvalidOperationException("MicroEngine cannot be mutated while it is executing.");
        
        EnterReentrancyScope();
        
        try
        {
            bool shouldActivate;
            lock (_gate)
            {
                ThrowIfEngineMutating();
                
                if (_objects.Contains(microObject))
                    return false;
                
                if (microObject.LifeCycleState == MicroLifeCycleState.Disposed)
                    throw new ObjectDisposedException(nameof(MicroObject));
                
                shouldActivate = State == MicroEngineState.Running;
            }
            
            await microObject.AttachToEngineAsync(this, cancellationToken);
            
            lock (_gate)
            {
                _objects.Add(microObject);
            }
            
            WriteTrace($"Registered object {microObject.GetType().Name}.");
            
            if (shouldActivate)
            {
                await microObject.ActivateAsync(cancellationToken);
                
                bool shouldRollback;
                lock (_gate)
                {
                    shouldRollback = State != MicroEngineState.Running;
                }
                
                if (shouldRollback)
                {
                    await microObject.DeactivateAsync(CancellationToken.None);
                    
                    Exception? detachException = null;
                    
                    lock (_gate)
                    {
                        _objects.Remove(microObject);
                    }
                    
                    try
                    {
                        await microObject.DetachFromEngineAsync(this, CancellationToken.None);
                    }
                    catch (Exception cleanupException)
                    {
                        detachException = cleanupException;
                    }
                    
                    WriteTrace($"Rolled back object registration for {microObject.GetType().Name}.");
                    
                    if (detachException is not null)
                        throw detachException;
                    
                    return false;
                }
                
                TryRegisterTickable(microObject);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Exception? detachException = null;
            
            lock (_gate)
            {
                _objects.Remove(microObject);
            }
            
            try
            {
                await microObject.DetachFromEngineAsync(this, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                detachException = cleanupException;
            }
            
            if (detachException is null)
                throw;
            
            throw new AggregateException(ex, detachException);
        }
        finally
        {
            ExitExecutionScope();
        }
    }
    
    /// <summary>从引擎注销对象；若引擎正在运行则先 drain 其 Tick，再停用该对象。</summary>
    public async ValueTask<bool> UnregisterObjectAsync(MicroObject microObject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(microObject);
        ThrowIfReentered();
        
        if (!await TryEnterExecutionScopeAsync(cancellationToken))
            throw new InvalidOperationException("MicroEngine cannot be mutated while it is executing.");
        
        EnterReentrancyScope();
        
        try
        {
            bool shouldDeactivate;
            
            lock (_gate)
            {
                ThrowIfEngineMutating();
                
                if (!_objects.Contains(microObject))
                    return false;
                
                shouldDeactivate = State is MicroEngineState.Running or MicroEngineState.Faulted;
            }
            
            await DrainTickableAsync(microObject, cancellationToken);
            
            if (shouldDeactivate)
            {
                try
                {
                    await microObject.DeactivateAsync(cancellationToken);
                }
                catch
                {
                    // 只有在对象仍然处于 Active 且仍在 _objects 列表中时才重新注册 tickable。
                    // 若已被异步路径从集合中移除，重新注册会让它在下一轮 tick 循环被"复活"，
                    // 导致不完整状态被继续调度。
                    bool stillRegistered;
                    lock (_gate)
                    {
                        stillRegistered = _objects.Contains(microObject);
                    }
                    
                    if (stillRegistered && microObject is IMicroTickable && microObject.LifeCycleState == MicroLifeCycleState.Active)
                        TryRegisterTickable(microObject, clearIsolation: false);
                    
                    throw;
                }
            }
            
            lock (_gate)
            {
                if (!_objects.Remove(microObject))
                    return false;
            }
            
            await microObject.DetachFromEngineAsync(this, CancellationToken.None);
            WriteTrace($"Unregistered object {microObject.GetType().Name}.");
            return true;
        }
        finally
        {
            ExitExecutionScope();
        }
    }
    
    /// <summary>动态注册服务；若引擎已在运行则立即启动该服务，失败时自动回滚。</summary>
    public async ValueTask<bool> RegisterServiceAsync(MicroService service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ThrowIfReentered();
        
        if (!await TryEnterExecutionScopeAsync(cancellationToken))
            throw new InvalidOperationException("MicroEngine cannot be mutated while it is executing.");
        
        EnterReentrancyScope();
        
        bool attached = false;
        
        try
        {
            bool shouldStart;
            lock (_gate)
            {
                ThrowIfEngineMutating();
                
                if (_services.Contains(service))
                    return false;
                
                shouldStart = State == MicroEngineState.Running;
            }
            
            await service.AttachToEngineAsync(this, cancellationToken);
            attached = true;
            
            lock (_gate)
            {
                _services.Add(service);
            }
            
            WriteTrace($"Registered service {service.GetType().Name}.");
            
            if (shouldStart)
            {
                await service.StartNodeAsync(cancellationToken);
                
                bool shouldRollback;
                lock (_gate)
                {
                    shouldRollback = State != MicroEngineState.Running;
                }
                
                if (shouldRollback)
                {
                    await service.StopNodeAsync(CancellationToken.None);
                    
                    Exception? detachException = null;
                    
                    lock (_gate)
                    {
                        _services.Remove(service);
                    }
                    
                    try
                    {
                        await service.DetachFromEngineAsync(this, CancellationToken.None);
                    }
                    catch (Exception cleanupException)
                    {
                        detachException = cleanupException;
                    }
                    
                    WriteTrace($"Rolled back service registration for {service.GetType().Name}.");
                    
                    if (detachException is not null)
                        throw detachException;
                    
                    return false;
                }
                
                TryRegisterTickable(service);
            }
            
            return true;
        }
        catch (Exception ex)
        {
            if (!attached)
                throw;
            
            List<Exception> rollbackErrors = [];
            
            try
            {
                await service.StopNodeAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                rollbackErrors.Add(rollbackException);
                MarkFaulted();
            }
            
            bool shouldDetach = false;
            
            lock (_gate)
            {
                if (rollbackErrors.Count == 0 || service.State == MicroServiceState.Stopped)
                {
                    _services.Remove(service);
                    shouldDetach = ReferenceEquals(service.Engine, this);
                }
            }
            
            if (shouldDetach)
            {
                try
                {
                    await service.DetachFromEngineAsync(this, CancellationToken.None);
                }
                catch (Exception detachException)
                {
                    rollbackErrors.Add(detachException);
                }
            }
            
            if (rollbackErrors.Count == 0)
                throw;
            
            rollbackErrors.Insert(0, ex);
            throw new AggregateException(rollbackErrors);
        }
        finally
        {
            ExitExecutionScope();
        }
    }
    
    /// <summary>动态注销服务；若引擎正在运行则先 drain 其 Tick，再停止该服务。</summary>
    public async ValueTask<bool> UnregisterServiceAsync(MicroService service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ThrowIfReentered();
        
        if (!await TryEnterExecutionScopeAsync(cancellationToken))
            throw new InvalidOperationException("MicroEngine cannot be mutated while it is executing.");
        
        EnterReentrancyScope();
        
        try
        {
            bool shouldStop;
            
            lock (_gate)
            {
                ThrowIfEngineMutating();
                
                if (!_services.Contains(service))
                    return false;
                
                shouldStop = State is MicroEngineState.Running or MicroEngineState.Faulted;
            }
            
            await DrainTickableAsync(service, cancellationToken);
            
            if (shouldStop)
            {
                try
                {
                    await service.StopNodeAsync(cancellationToken);
                }
                catch
                {
                    if (CanScheduleServiceTicking(service))
                        TryRegisterTickable(service, clearIsolation: false);
                    
                    throw;
                }
            }
            
            lock (_gate)
            {
                if (!_services.Remove(service))
                    return false;
            }
            
            await service.DetachFromEngineAsync(this, CancellationToken.None);
            WriteTrace($"Unregistered service {service.GetType().Name}.");
            return true;
        }
        finally
        {
            ExitExecutionScope();
        }
    }
    
    /// <summary>根据当前活动节点重建 Tickable 注册表。</summary>
    private void RebuildTickableRegistry()
    {
        TickableRegistrationSnapshot[] snapshot;
        
        lock (_gate)
        {
            List<TickableRegistrationSnapshot> items = [];
            
            foreach (MicroService service in _services.Where(CanScheduleServiceTicking))
            {
                if (service is IMicroTickable tickable)
                    items.Add(new TickableRegistrationSnapshot(tickable, service.Order, service.GetType().Name));
            }
            
            foreach (MicroObject microObject in _objects.Where(static microObject => microObject.LifeCycleState == MicroLifeCycleState.Active))
            {
                if (microObject is IMicroTickable tickable)
                    items.Add(new TickableRegistrationSnapshot(tickable, 0, microObject.GetType().Name));
            }
            
            snapshot = items.OrderBy(static item => item.Order).ToArray();
        }
        
        _tickScheduler.Rebuild(snapshot);
    }
    
    /// <summary>尝试将活动对象或服务重新注册到调度器。</summary>
    private void TryRegisterTickable(object candidate, bool clearIsolation = true)
    {
        switch (candidate)
        {
            case MicroService service when service is IMicroTickable tickable && CanScheduleServiceTicking(service):
                _tickScheduler.Register(tickable, service.Order, service.GetType().Name, clearIsolation);
                break;
            case MicroObject { LifeCycleState: MicroLifeCycleState.Active } microObject when microObject is IMicroTickable tickable:
                _tickScheduler.Register(tickable, 0, microObject.GetType().Name, clearIsolation);
                break;
        }
    }
    
    /// <summary>在对象保持活动状态时恢复其 Tick 注册。</summary>
    internal void RegisterActiveObjectTicking(MicroObject microObject)
    {
        ArgumentNullException.ThrowIfNull(microObject);
        
        bool shouldRegister;
        lock (_gate)
        {
            shouldRegister = _objects.Contains(microObject) && State is MicroEngineState.Running or MicroEngineState.Faulted;
        }
        
        if (shouldRegister)
            TryRegisterTickable(microObject);
    }
    
    /// <summary>在对象停用前暂停并清空其 Tick 调度。</summary>
    internal async ValueTask SuspendObjectTickingAsync(MicroObject microObject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(microObject);
        
        bool shouldDrain;
        lock (_gate)
        {
            shouldDrain = _objects.Contains(microObject) && State is MicroEngineState.Running or MicroEngineState.Faulted;
        }
        
        if (shouldDrain)
            await DrainTickableAsync(microObject, cancellationToken);
    }
    
    /// <summary>等待指定 Tickable 排空并从调度队列移除。</summary>
    private async ValueTask DrainTickableAsync(object candidate, CancellationToken cancellationToken)
    {
        if (candidate is not IMicroTickable tickable)
            return;
        
        await _tickScheduler.DrainAndRemoveAsync(tickable, cancellationToken);
    }
    
    /// <summary>判断服务当前是否仍满足 Tick 调度资格。</summary>
    private bool CanScheduleServiceTicking(MicroService service) => ReferenceEquals(service.Engine, this) && !service.IsDisposed && service.LifeCycleState == MicroLifeCycleState.Active;
    
    /// <summary>在引擎处于启动或停止阶段时阻止结构变更。</summary>
    private void ThrowIfEngineMutating()
    {
        if (State is MicroEngineState.Starting or MicroEngineState.Stopping)
            throw new InvalidOperationException($"MicroEngine cannot be mutated while it is '{State}'.");
    }
    
    /// <summary>在当前异步流已持有执行门时阻止重入。</summary>
    private void ThrowIfReentered()
    {
        if (IsWithinActiveExecutionScope() || _objectLifecycleGate.Value is { IsActive: true })
            throw new InvalidOperationException("MicroEngine cannot be re-entered while it is executing.");
    }
    
    /// <summary>判断当前异步流是否处于活动执行作用域内。</summary>
    private bool IsWithinActiveExecutionScope() => _executionScope.Value is { IsActive: true, Depth: > 0 };
    
    /// <summary>进入当前异步流的可重入执行作用域。</summary>
    private void EnterReentrancyScope(bool ownsExecutionGate = true)
    {
        ExecutionScopeState? scope = _executionScope.Value;
        if (scope is null || !scope.IsActive)
        {
            scope = new ExecutionScopeState { OwnsExecutionGate = ownsExecutionGate, };
            _executionScope.Value = scope;
        }
        else if (ownsExecutionGate)
        {
            scope.OwnsExecutionGate = true;
        }
        
        scope.Depth++;
    }
    
    /// <summary>尝试同步获取执行门。</summary>
    private bool TryEnterExecutionScope() => _executionGate.Wait(0);
    
    /// <summary>尝试异步获取执行门。</summary>
    private ValueTask<bool> TryEnterExecutionScopeAsync(CancellationToken cancellationToken) => new(TryEnterExecutionScopeAsyncCore(cancellationToken));
    
    /// <summary>执行异步尝试获取执行门的实际逻辑。</summary>
    private async Task<bool> TryEnterExecutionScopeAsyncCore(CancellationToken cancellationToken) => await _executionGate.WaitAsync(0, cancellationToken);
    
    /// <summary>异步等待获取执行门。</summary>
    private async ValueTask EnterExecutionScopeAsync(CancellationToken cancellationToken) => await _executionGate.WaitAsync(cancellationToken);
    
    /// <summary>为销毁路径获取执行门；启动中等待完成，其余状态快速失败。</summary>
    private async ValueTask EnterDisposalExecutionScopeAsync()
    {
        bool waitForStartup;
        lock (_gate)
        {
            waitForStartup = State == MicroEngineState.Starting;
        }
        
        if (waitForStartup)
        {
            await EnterExecutionScopeAsync(CancellationToken.None);
            return;
        }
        
        if (!TryEnterExecutionScope())
            throw new InvalidOperationException("MicroEngine cannot be mutated while it is executing.");
    }
    
    /// <summary>为公开对象生命周期操作获取专用执行门。</summary>
    internal async ValueTask<bool> EnterObjectLifecycleScopeAsync(CancellationToken cancellationToken)
    {
        ExecutionScopeState? scope = _executionScope.Value;
        if (scope is { IsActive: true, Depth: > 0 })
        {
            if (scope.OwnsExecutionGate)
                return false;
            
            throw new InvalidOperationException("Registered object lifecycle cannot be changed from within tick execution.");
        }
        
        if (_objectLifecycleGate.Value is { IsActive: true })
            throw new InvalidOperationException("MicroEngine cannot be re-entered while it is executing.");
        
        await EnterExecutionScopeAsync(cancellationToken);
        _objectLifecycleGate.Value = new ObjectLifecycleGateState();
        return true;
    }
    
    /// <summary>
    /// 释放对象生命周期操作持有的执行门。仅当当前异步流持有有效的 scope 时
    /// 才会释放执行门，保证与 <see cref="EnterObjectLifecycleScopeAsync"/> 一一对应，
    /// 避免重复调用触发 <see cref="SemaphoreFullException"/>。
    /// </summary>
    /// <summary>
    /// 释放对象生命周期操作持有的执行门。
    /// </summary>
    /// <remarks>
    /// 注意：此处采用**无条件** <see cref="SemaphoreSlim.Release"/>，与
    /// <see cref="EnterObjectLifecycleScopeAsync"/> 返回的 <c>engineScopeAcquired</c>
    /// 标志配对使用。调用方必须保证只有在 Enter 成功返回 <c>true</c> 时才调用本方法。
    ///
    /// 不在此做 <c>scope is null</c> 的自防御早返回：由于 .NET 的 async 方法通过
    /// <c>AsyncTaskMethodBuilder.Start</c> 的 finally 会在方法同步完成时还原
    /// <see cref="ExecutionContext"/>，内部对 <see cref="AsyncLocal{T}.Value"/>
    /// 的赋值并不会传播到调用者的 EC。当 <c>EnterExecutionScopeAsync</c> 同步完成
    /// （gate 可直接获取）时，调用方看到的 <c>_objectLifecycleGate.Value</c> 仍为
    /// <c>null</c>。若此处以 scope 是否非空作为 Release 的前提，就会导致 gate
    /// 永远无法释放、后续调用全部抛出 "cannot be mutated while it is executing"。
    /// </remarks>
    internal void ExitObjectLifecycleScope()
    {
        ObjectLifecycleGateState? scope = _objectLifecycleGate.Value;
        if (scope is not null)
        {
            scope.IsActive = false;
            if (ReferenceEquals(_objectLifecycleGate.Value, scope))
                _objectLifecycleGate.Value = null;
        }
        
        _executionGate.Release();
    }
    
    /// <summary>
    /// 退出完整执行作用域并（仅当嵌套深度归零时）释放执行门。
    /// 不变式：<see cref="EnterExecutionScopeAsync"/> 成对地 <c>Wait</c>/<c>Release</c> 一次，
    /// 嵌套的 <see cref="EnterReentrancyScope"/> 只推进 Depth 而不影响信号量计数。
    /// </summary>
    private void ExitExecutionScope()
    {
        ExecutionScopeState? scope = _executionScope.Value;
        if (scope is null || !scope.IsActive || scope.Depth <= 0)
            throw new InvalidOperationException("MicroEngine execution scope is not active.");
        
        scope.Depth--;
        if (scope.Depth == 0)
        {
            scope.IsActive = false;
            if (ReferenceEquals(_executionScope.Value, scope))
                _executionScope.Value = null;
            
            _executionGate.Release();
        }
    }
    
    /// <summary>退出不拥有执行门的重入作用域。</summary>
    private void ExitReentrancyScopeWithoutGate()
    {
        ExecutionScopeState? scope = _executionScope.Value;
        if (scope is null || !scope.IsActive || scope.Depth <= 0)
            throw new InvalidOperationException("MicroEngine execution scope is not active.");
        
        scope.Depth--;
        if (scope.Depth == 0)
        {
            scope.IsActive = false;
            if (ReferenceEquals(_executionScope.Value, scope))
                _executionScope.Value = null;
        }
    }
    
    /// <summary>清空当前异步流继承到的执行作用域状态。</summary>
    private void ClearExecutionScopeForCurrentFlow() => _executionScope.Value = null;
    
    /// <summary>在引擎上下文内销毁一个已注册对象。</summary>
    internal async ValueTask DisposeObjectAsync(MicroObject microObject)
    {
        ArgumentNullException.ThrowIfNull(microObject);
        
        if (Volatile.Read(ref _disposed) == 1)
        {
            await microObject.DisposeCoreAsync();
            return;
        }
        
        if (IsWithinActiveExecutionScope())
            throw new InvalidOperationException("MicroEngine cannot be mutated while it is executing.");
        
        await EnterDisposalExecutionScopeAsync();
        
        EnterReentrancyScope();
        try
        {
            bool wasRegistered;
            List<Exception> errors = [];
            
            lock (_gate)
            {
                wasRegistered = _objects.Contains(microObject);
            }
            
            try
            {
                if (wasRegistered)
                    await DrainTickableAsync(microObject, CancellationToken.None);
                
                await microObject.DisposeCoreAsync();
            }
            catch (Exception ex)
            {
                FlattenInto(errors, ex);
                
                if (wasRegistered && microObject is IMicroTickable && microObject.LifeCycleState == MicroLifeCycleState.Active)
                    TryRegisterTickable(microObject, clearIsolation: false);
            }
            
            if (wasRegistered && microObject.LifeCycleState == MicroLifeCycleState.Disposed)
            {
                lock (_gate)
                {
                    _objects.Remove(microObject);
                }
                
                try
                {
                    await microObject.DetachFromEngineAsync(this, CancellationToken.None);
                }
                catch (Exception detachException)
                {
                    FlattenInto(errors, detachException);
                }
            }
            
            ThrowIfNeeded(errors);
        }
        finally
        {
            ExitExecutionScope();
        }
    }
    
    /// <summary>在引擎上下文内销毁一个已注册服务。</summary>
    internal async ValueTask DisposeServiceAsync(MicroService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        
        if (Volatile.Read(ref _disposed) == 1)
        {
            await service.DisposeCoreAsync();
            return;
        }
        
        if (IsWithinActiveExecutionScope())
            throw new InvalidOperationException("MicroEngine cannot be mutated while it is executing.");
        
        await EnterDisposalExecutionScopeAsync();
        
        EnterReentrancyScope();
        try
        {
            bool wasRegistered;
            List<Exception> errors = [];
            
            lock (_gate)
            {
                wasRegistered = _services.Contains(service);
            }
            
            try
            {
                if (wasRegistered)
                    await DrainTickableAsync(service, CancellationToken.None);
                
                await service.DisposeCoreAsync();
            }
            catch (Exception ex)
            {
                FlattenInto(errors, ex);
                
                if (wasRegistered && CanScheduleServiceTicking(service))
                    TryRegisterTickable(service, clearIsolation: false);
            }
            
            if (wasRegistered && service.IsDisposed)
            {
                lock (_gate)
                {
                    _services.Remove(service);
                }
                
                try
                {
                    await service.DetachFromEngineAsync(this, CancellationToken.None);
                }
                catch (Exception detachException)
                {
                    FlattenInto(errors, detachException);
                }
            }
            
            ThrowIfNeeded(errors);
        }
        finally
        {
            ExitExecutionScope();
        }
    }
    
    /// <summary>将异常扁平化后追加到目标列表，避免 <see cref="AggregateException"/> 在销毁路径上被再次嵌套。</summary>
    internal static void FlattenInto(List<Exception> target, Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            AggregateException flattened = aggregate.Flatten();
            foreach (Exception inner in flattened.InnerExceptions)
                target.Add(inner);
        }
        else
        {
            target.Add(exception);
        }
    }
    
    /// <summary>根据收集到的异常数量统一抛出（单个保留堆栈，多个合并为 <see cref="AggregateException"/>）。</summary>
    private static void ThrowIfNeeded(List<Exception> errors)
    {
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        
        if (errors.Count > 1)
            throw new AggregateException(errors);
    }
    
    /// <summary>描述当前异步流的执行作用域状态。</summary>
    private sealed class ExecutionScopeState
    {
        /// <summary>当前作用域嵌套深度。</summary>
        public int Depth { get; set; }
        
        /// <summary>该作用域当前是否仍然有效。</summary>
        public bool IsActive { get; set; } = true;
        
        /// <summary>当前作用域是否拥有执行门。</summary>
        public bool OwnsExecutionGate { get; set; }
    }
    
    /// <summary>标记公开对象生命周期操作持有的执行门状态。</summary>
    private sealed class ObjectLifecycleGateState
    {
        /// <summary>该门状态当前是否仍然有效。</summary>
        public bool IsActive { get; set; } = true;
    }
    
    /// <summary>用于批量重建调度器注册表的快照项。</summary>
    private readonly record struct TickableRegistrationSnapshot(IMicroTickable Tickable, int Order, string DisplayName);
    
    /// <summary>引擎内部的 Tick 调度与后台循环执行器。</summary>
    private sealed class MicroTickSchedulerRunner
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(100);
        
        private readonly MicroEngine _owner;
        private readonly Lock _gate = new();
        private readonly Dictionary<IMicroTickable, TickableRegistration> _registrations = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<IMicroTickable> _isolatedTickables = new(ReferenceEqualityComparer.Instance);
        private Task? _runLoopTask;
        private CancellationTokenSource? _runLoopSignal;
        private bool _acceptFrames = true;
        private List<Exception>? _dispatchFailures;
        
        public MicroTickSchedulerRunner(MicroEngine owner)
        {
            _owner = owner;
        }
        
        /// <summary>使用快照内容整体重建当前注册表。</summary>
        public void Rebuild(IEnumerable<TickableRegistrationSnapshot> registrations)
        {
            lock (_gate)
            {
                _registrations.Clear();
                _isolatedTickables.Clear();
                _acceptFrames = true;
                
                foreach (TickableRegistrationSnapshot registration in registrations)
                {
                    _registrations[registration.Tickable] = new TickableRegistration(registration.Tickable, registration.Order, registration.DisplayName);
                }
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
                {
                    existing.Order = order;
                    existing.DisplayName = displayName;
                    existing.AcceptsFrames = true;
                    existing.LastException = null;
                    existing.PendingDelta = TimeSpan.Zero;
                    return;
                }
                
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
        
        /// <summary>分发一帧并等待本次帧处理完成。</summary>
        public async ValueTask DispatchAndWaitAsync(TimeSpan deltaTime, CancellationToken cancellationToken)
        {
            if (deltaTime <= TimeSpan.Zero)
                return;
            
            TickableRegistration[] registrations;
            Task[] waitTasks;
            
            lock (_gate)
            {
                _dispatchFailures = [];
            }
            
            try
            {
                waitTasks = EnqueueFrame(deltaTime, cancellationToken, out registrations);
                if (waitTasks.Length == 0)
                    return;
                
                await Task.WhenAll(waitTasks);
                
                List<Exception> failures;
                lock (_gate)
                {
                    failures = _dispatchFailures is null ? [] : [.. _dispatchFailures];
                }
                
                if (failures.Count == 1)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failures[0]).Throw();
                
                if (failures.Count > 1)
                    throw new AggregateException(failures);
                
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                lock (_gate)
                {
                    _dispatchFailures = null;
                }
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
        
        /// <summary>后台 Tick 循环当前是否仍在运行。</summary>
        public bool IsRunLoopActive
        {
            get
            {
                lock (_gate)
                {
                    return _runLoopTask is { IsCompleted: false };
                }
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
        private Task[] EnqueueFrame(TimeSpan deltaTime, CancellationToken dispatchCancellationToken) => EnqueueFrame(deltaTime, dispatchCancellationToken, out _);
        
        /// <summary>将一帧时间分发到当前活动注册项，并返回等待信息。</summary>
        private Task[] EnqueueFrame(TimeSpan deltaTime, CancellationToken dispatchCancellationToken, out TickableRegistration[] registrations)
        {
            lock (_gate)
            {
                if (!_acceptFrames)
                {
                    registrations = [];
                    return [];
                }
                
                registrations = _registrations.Values.Where(static registration => registration.AcceptsFrames).OrderBy(static registration => registration.Order).ToArray();
                
                if (registrations.Length == 0)
                {
                    return [];
                }
                
                foreach (TickableRegistration registration in registrations)
                {
                    registration.PendingDelta += deltaTime;
                    EnsureExecutionLocked(registration, dispatchCancellationToken);
                }
                
                return registrations.Select(static registration => registration.GetDrainTask()).ToArray();
            }
        }
        
        /// <summary>确保指定注册项已经拥有正在运行的执行任务。</summary>
        private void EnsureExecutionLocked(TickableRegistration registration, CancellationToken dispatchCancellationToken)
        {
            if (registration.IsExecuting || !registration.AcceptsFrames || registration.PendingDelta <= TimeSpan.Zero)
                return;
            
            registration.IsExecuting = true;
            registration.EnsureBusy();
            registration.ExecutionTask = Task.Run(() => ExecuteTickableAsync(registration, dispatchCancellationToken), CancellationToken.None);
        }
        
        /// <summary>执行单个注册项的实际 Tick 循环。</summary>
        private async Task ExecuteTickableAsync(TickableRegistration registration, CancellationToken dispatchCancellationToken)
        {
            _owner.ClearExecutionScopeForCurrentFlow();
            
            while (true)
            {
                TimeSpan deltaTime;
                
                lock (_gate)
                {
                    if (!registration.AcceptsFrames)
                    {
                        registration.PendingDelta = TimeSpan.Zero;
                        registration.IsExecuting = false;
                        registration.ExecutionTask = null;
                        registration.MarkIdle();
                        return;
                    }
                    
                    if (registration.PendingDelta <= TimeSpan.Zero)
                    {
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
                    _owner.EnterReentrancyScope(ownsExecutionGate: false);
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
                        _dispatchFailures?.Add(ex);
                    }
                    
                    _owner.WriteTrace($"Tickable {registration.DisplayName} failed: {ex.Message}");
                    return;
                }
                finally
                {
                    if (_owner.IsWithinActiveExecutionScope())
                        _owner.ExitReentrancyScopeWithoutGate();
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
        
        /// <summary>初始化一个新的 Tickable 注册项。</summary>
        public TickableRegistration(IMicroTickable tickable, int order, string displayName)
        {
            Tickable = tickable;
            Order = order;
            DisplayName = displayName;
        }
        
        /// <summary>注册项关联的 Tickable 实例。</summary>
        public IMicroTickable Tickable { get; }
        
        /// <summary>调度顺序值。</summary>
        public int Order { get; set; }
        
        /// <summary>调试用显示名称。</summary>
        public string DisplayName { get; set; }
        
        /// <summary>当前是否仍接受新的帧累积。</summary>
        public bool AcceptsFrames { get; set; } = true;
        
        /// <summary>当前是否已有执行任务在运行。</summary>
        public bool IsExecuting { get; set; }
        
        /// <summary>尚未消费的累计帧时间。</summary>
        public TimeSpan PendingDelta { get; set; }
        
        /// <summary>最近一次执行异常。</summary>
        public Exception? LastException { get; set; }
        
        /// <summary>当前关联的执行任务。</summary>
        public Task? ExecutionTask { get; set; }
        
        /// <summary>确保当前注册项已进入忙碌状态。</summary>
        public void EnsureBusy()
        {
            if (_idleSignal.Task.IsCompleted)
                _idleSignal = CreatePendingSignal();
        }
        
        /// <summary>获取当前注册项进入空闲状态的等待任务。</summary>
        public Task GetDrainTask()
        {
            if (!IsExecuting && PendingDelta <= TimeSpan.Zero)
                return Task.CompletedTask;
            
            EnsureBusy();
            return _idleSignal.Task;
        }
        
        /// <summary>将当前注册项标记为空闲。</summary>
        public void MarkIdle()
        {
            _idleSignal.TrySetResult(true);
        }
        
        /// <summary>创建一个已完成的空闲信号。</summary>
        private static TaskCompletionSource<bool> CreateCompletedSignal()
        {
            TaskCompletionSource<bool> signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            signal.TrySetResult(true);
            return signal;
        }
        
        /// <summary>创建一个待完成的空闲信号。</summary>
        private static TaskCompletionSource<bool> CreatePendingSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    
    /// <summary>
    /// 异步释放引擎。内部先尽力而为调用 <see cref="StopAsync"/>（吞掉异常并记入 trace），
    /// 再释放 <see cref="_executionGate"/> 与调度器的 <see cref="CancellationTokenSource"/> 等本地句柄。
    /// 多次调用等价于一次（幂等）。
    /// </summary>
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
                await StopAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                WriteTrace($"Engine dispose: StopAsync threw and was swallowed: {ex.GetType().Name}: {ex.Message}");
            }
        }
        
        try
        {
            _tickScheduler.Dispose();
        }
        catch (Exception ex)
        {
            WriteTrace($"Engine dispose: tick scheduler dispose threw and was swallowed: {ex.GetType().Name}: {ex.Message}");
        }
        
        try
        {
            _executionGate.Dispose();
        }
        catch (Exception ex)
        {
            WriteTrace($"Engine dispose: execution gate dispose threw and was swallowed: {ex.GetType().Name}: {ex.Message}");
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
    
    /// <summary>写入引擎级跟踪日志。</summary>
    private void WriteTrace(string message)
    {
        Logger.LogDebug(message);
    }
}