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
    private readonly MicroEvent _events = new();
    private readonly List<MicroObject> _objects = [];
    private readonly List<MicroService> _services = [];
    private readonly MicroTickSchedulerRunner _tickScheduler;
    private IMicroLogger? _logger;
    
    /// <summary>
    /// 需要在<see cref="MicroLifeCycle.OnAttachedAsync"/> 之后访问
    /// </summary>
    public static MicroEngine Instance { get; private set; } = null!;
    
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
    /// </summary>
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
    
    /// <summary>从已注册服务中查找，不存在时返回 null。</summary>
    public T? GetService<T>() where T : class
    {
        lock (_gate)
        {
            return _services.OfType<T>().FirstOrDefault();
        }
    }
    
    /// <summary>从已注册服务中查找，不存在时抛出异常。</summary>
    public T GetRequiredService<T>() where T : class => GetService<T>() ?? throw new InvalidOperationException($"Service '{typeof(T).FullName}' is not registered.");
    
    /// <summary>Subscribes to an event type on this engine only.</summary>
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
    
    /// <summary>Publishes an event to subscribers registered for the event instance runtime type on this engine only.</summary>
    public ValueTask PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default) where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ThrowIfDisposed();
        return _events.PublishAsync(domainEvent, cancellationToken);
    }
    
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
    public async ValueTask StartEngine(CancellationToken cancellationToken = default)
    {
        try
        {
            if (State == MicroEngineState.Running)
                return;
            
            if (State != MicroEngineState.Stopped)
                throw new InvalidOperationException($"MicroEngine cannot start while it is '{State}'.");
            
            State = MicroEngineState.Starting;
            WriteTrace("Engine starting.");
            State = MicroEngineState.Running;
            
            _ = _tickScheduler.RunAsync(cancellationToken);
            WriteTrace("Engine started.");
            await Task.CompletedTask;
        }
        catch
        {
            MarkFaulted();
            throw;
        }
    }
    
    /// <summary>
    /// 停止引擎：先冻结并 drain 所有 Tickable，再按逆序停用对象与服务。
    /// 收集所有错误后统一抛出，保证所有清理步骤都有机会执行。
    /// </summary>
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
        }
        
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        
        if (errors.Count > 1)
            throw new AggregateException(errors);
    }
    
    /// <summary>向引擎注册对象；若引擎已在运行则立即激活该对象。</summary>
    public async ValueTask<bool> RegisterObjectAsync(MicroObject microObject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(microObject);
        ThrowIfEngineNotRunning();
        try
        {
            lock (_gate)
            {
                if (_objects.Contains(microObject))
                    return false;
                
                if (microObject.LifeCycleState == MicroLifeCycleState.Disposed)
                    throw new ObjectDisposedException(nameof(MicroObject));
            }
            
            await microObject.AttachToEngineAsync(this, cancellationToken);
            
            lock (_gate)
            {
                _objects.Add(microObject);
            }
            
            WriteTrace($"Registered object {microObject.GetType().Name}.");
            
            await microObject.ActivateAsync(cancellationToken);
            
            TryRegisterTickable(microObject);
            
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
    }
    
    /// <summary>从引擎注销对象；若引擎正在运行则先 drain 其 Tick，再停用该对象。</summary>
    public async ValueTask<bool> UnregisterObjectAsync(MicroObject microObject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(microObject);
        
        try
        {
            lock (_gate)
            {
                ThrowIfEngineMutating();
                
                if (!_objects.Contains(microObject))
                    return false;
            }
            
            await DrainTickableAsync(microObject, cancellationToken);
            
            
            try
            {
                if (State is MicroEngineState.Running or MicroEngineState.Faulted)
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
            
        }
    }
    
    /// <summary>动态注册服务；若引擎已在运行则立即启动该服务，失败时自动回滚。</summary>
    public async ValueTask<bool> RegisterServiceAsync(MicroService service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ThrowIfEngineNotRunning();
        bool attached = false;
        try
        {
            lock (_gate)
            {
                if (_services.Contains(service))
                    return false;
                
            }
            await service.AttachToEngineAsync(this, cancellationToken);
            attached = true;
            
            lock (_gate)
            {
                _services.Add(service);
            }
            
            WriteTrace($"Registered service {service.GetType().Name}.");
            
            await service.StartNodeAsync(cancellationToken);
            
            TryRegisterTickable(service);
            
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
    }
    
    /// <summary>动态注销服务；若引擎正在运行则先 drain 其 Tick，再停止该服务。</summary>
    public async ValueTask<bool> UnregisterServiceAsync(MicroService service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        
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
            
        }
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
    private void ThrowIfEngineNotRunning()
    {
        if (State != MicroEngineState.Running)
            throw new InvalidOperationException($"引擎不在运行，无法执行此操作。");
    }
    
    /// <summary>在引擎上下文内销毁一个已注册对象。</summary>
    internal async ValueTask DisposeObjectAsync(MicroObject microObject)
    {
        ArgumentNullException.ThrowIfNull(microObject);
        
        if (Volatile.Read(ref _disposed) == 1)
        {
            await microObject.DisposeCoreAsync();
            return;
        }
        
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
                    throw new InvalidOperationException($"Tickable '{displayName}' is already registered with order {existing.Order}.") { Data = { ["Existing"] = existing, ["New"] = new TickableRegistration(tickable, order, displayName) } };
                
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
                finally
                {
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
            TickInterval = TimeSpan.FromSeconds(1.0 / Math.Clamp(tickable.Frame, 1u, 120u));
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
        /// <summary>
        /// 帧间隔时间。引擎将以此频率调用 <see cref="Tickable"/> 的 <see cref="IMicroTickable.TickAsync"/> 方法。
        /// </summary>
        public TimeSpan TickInterval { get; set; }
        
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
    /// 异步释放引擎。内部先尽力而为调用 <see cref="StopEngine"/>（吞掉异常并记入 trace），
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
    
    /// <summary>写入引擎级跟踪日志。</summary>
    private void WriteTrace(string message)
    {
        Logger.LogDebug(message);
    }
    
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) == 1)
            throw new ObjectDisposedException(nameof(MicroEngine));
    }
}