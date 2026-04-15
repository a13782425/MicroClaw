namespace MicroClaw.Core;
/// <summary>引擎生命周期状态。</summary>
public enum MicroEngineState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    /// <summary>启动/停止/Tick 过程中出现异常，需手动恢复。</summary>
    Faulted,
}
/// <summary>
/// 核心运行时，负责管理 <see cref="MicroObject"/> 和 <see cref="MicroService"/> 的生命周期。
/// 通过执行门（execution gate）保证 Start / Stop / Tick 的线程安全。
/// </summary>
public sealed class MicroEngine
{
    private readonly Lock _gate = new(); // 保护 _objects、_services 列表及 State 字段的互斥锁
    private readonly SemaphoreSlim _executionGate = new(1, 1); // 限制同一时刻只有一个调用方执行引擎操作
    private readonly AsyncLocal<ExecutionScopeState?> _executionScope = new(); // 追踪当前异步流的可重入深度
    private readonly IServiceProvider _serviceProvider;
    private readonly List<MicroObject> _objects = [];
    private readonly List<MicroService> _services = [];
    
    /// <summary>初始化引擎并批量挂载初始服务；若任意服务挂载失败则回滚已挂载的全部服务。</summary>
    public MicroEngine(IServiceProvider serviceProvider, IEnumerable<MicroService> services)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        
        List<MicroService> attachedServices = [];
        
        try
        {
            foreach (MicroService service in services)
            {
                AttachService(service);
                attachedServices.Add(service);
            }
        }
        catch
        {
            // 回滚：按逆序卸载已成功挂载的服务
            foreach (MicroService service in attachedServices.AsEnumerable().Reverse())
            {
                _services.Remove(service);
                service.DetachFromEngine(this);
            }
            
            throw;
        }
    }
    
    /// <summary>当前引擎状态。</summary>
    public MicroEngineState State { get; private set; } = MicroEngineState.Stopped;
    
    /// <summary>引擎是否处于 Running 状态。</summary>
    public bool IsStarted => State == MicroEngineState.Running;
    
    /// <summary>从 DI 容器获取指定类型的服务，不存在时返回 null。</summary>
    public T? GetService<T>() where T : class => _serviceProvider.GetService(typeof(T)) as T;
    
    /// <summary>从 DI 容器获取指定类型的服务，不存在时抛出异常。</summary>
    public T GetRequiredService<T>() where T : class => GetService<T>() ?? throw new InvalidOperationException($"Service '{typeof(T).FullName}' is not registered.");
    
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
                serviceSnapshot = _services.OrderBy(static service => service.Order).ToArray();
                objectSnapshot = _objects.ToArray();
            }
            
            List<MicroService> startedServices = [];
            List<MicroObject> activatedObjects = [];
            
            try
            {
                foreach (MicroService service in serviceSnapshot)
                {
                    startedServices.Add(service);
                    await service.StartInternalAsync(cancellationToken);
                }
                
                foreach (MicroObject microObject in objectSnapshot)
                {
                    lock (_gate)
                    {
                        if (!_objects.Contains(microObject) || microObject.State == MicroObjectState.Disposed)
                            continue;
                    }
                    
                    microObject.Activate();
                    activatedObjects.Add(microObject);
                }
                
                lock (_gate)
                {
                    State = MicroEngineState.Running;
                }
            }
            catch (Exception startException)
            {
                List<Exception> rollbackErrors = [];
                
                foreach (MicroObject microObject in activatedObjects.AsEnumerable().Reverse())
                {
                    try
                    {
                        microObject.Deactivate();
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
                        await service.StopInternalAsync(CancellationToken.None);
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
    /// 停止引擎：按逆序停用所有对象，再按逆序停止所有服务。
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
                serviceSnapshot = _services.OrderBy(static service => service.Order).ToArray();
                objectSnapshot = _objects.ToArray();
            }
            foreach (MicroObject microObject in objectSnapshot.Reverse())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    errors.Add(new OperationCanceledException(cancellationToken));
                    break;
                }
                
                try
                {
                    microObject.Deactivate();
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
                    await service.StopInternalAsync(cancellationToken);
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
            
            if (gateAcquired)
                ExitExecutionScope();
        }
        
        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        
        if (errors.Count > 1)
            throw new AggregateException(errors);
    }
    
    /// <summary>驱动一帧更新，按 Order 顺序调用所有已启动的 <see cref="MicroUpdateService"/>。</summary>
    public async ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
    {
        if (deltaTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "Tick delta time must be non-negative.");
        
        ThrowIfReentered();
        
        await EnterExecutionScopeAsync(cancellationToken);
        EnterReentrancyScope();
        try
        {
            MicroUpdateService[] updates;
            lock (_gate)
            {
                if (State != MicroEngineState.Running)
                    throw new InvalidOperationException("MicroEngine must be started before ticking.");
                
                updates = _services.OfType<MicroUpdateService>().Where(static service => service.IsStarted).OrderBy(static service => service.Order).ToArray();
            }
            
            foreach (MicroUpdateService update in updates)
                await update.TickAsync(deltaTime, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            MarkFaulted();
            throw;
        }
        catch
        {
            MarkFaulted();
            throw;
        }
        finally
        {
            ExitExecutionScope();
        }
    }
    
    /// <summary>向引擎注册对象；若引擎已在运行则立即激活该对象。</summary>
    public bool RegisterObject(MicroObject microObject)
    {
        ArgumentNullException.ThrowIfNull(microObject);
        
        if (!TryEnterExecutionScope())
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
                
                if (microObject.State == MicroObjectState.Disposed)
                    throw new ObjectDisposedException(nameof(MicroObject));
                
                microObject.AttachToEngine(this);
                _objects.Add(microObject);
                shouldActivate = State == MicroEngineState.Running;
            }
            
            if (shouldActivate)
            {
                microObject.Activate();
                
                bool shouldRollback;
                lock (_gate)
                {
                    shouldRollback = State != MicroEngineState.Running;
                }
                
                if (shouldRollback)
                {
                    microObject.Deactivate();
                    lock (_gate)
                    {
                        _objects.Remove(microObject);
                        microObject.DetachFromEngine(this);
                    }
                    return false;
                }
            }
            
            return true;
        }
        catch
        {
            lock (_gate)
            {
                _objects.Remove(microObject);
                microObject.DetachFromEngine(this);
            }
            
            throw;
        }
        finally
        {
            ExitExecutionScope();
        }
    }
    
    /// <summary>从引擎注销对象；若引擎正在运行则先停用该对象。</summary>
    public bool UnregisterObject(MicroObject microObject)
    {
        ArgumentNullException.ThrowIfNull(microObject);
        
        if (!TryEnterExecutionScope())
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
            
            if (shouldDeactivate)
                microObject.Deactivate();
            
            lock (_gate)
            {
                if (!_objects.Remove(microObject))
                    return false;
            }
            
            microObject.DetachFromEngine(this);
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
                
                AttachService(service);
                attached = true;
                shouldStart = State == MicroEngineState.Running;
            }
            
            if (shouldStart)
            {
                await service.StartInternalAsync(cancellationToken);
                
                bool shouldRollback;
                lock (_gate)
                {
                    shouldRollback = State != MicroEngineState.Running;
                }
                
                if (shouldRollback)
                {
                    await service.StopInternalAsync(CancellationToken.None);
                    lock (_gate)
                    {
                        _services.Remove(service);
                        service.DetachFromEngine(this);
                    }
                    return false;
                }
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
                await service.StopInternalAsync(CancellationToken.None);
            }
            catch (Exception rollbackException)
            {
                rollbackErrors.Add(rollbackException);
                MarkFaulted();
            }
            
            lock (_gate)
            {
                if (rollbackErrors.Count == 0 || service.State == MicroServiceState.Stopped)
                {
                    _services.Remove(service);
                    service.DetachFromEngine(this);
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
    
    /// <summary>动态注销服务；若引擎正在运行则先停止该服务。</summary>
    public async ValueTask<bool> UnregisterServiceAsync(MicroService service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        
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
            
            if (shouldStop)
                await service.StopInternalAsync(cancellationToken);
            
            lock (_gate)
            {
                if (!_services.Remove(service))
                    return false;
            }
            
            service.DetachFromEngine(this);
            return true;
        }
        finally
        {
            ExitExecutionScope();
        }
    }
    
    private void AttachService(MicroService service)
    {
        if (_services.Contains(service))
            return;
        
        service.AttachToEngine(this);
        _services.Add(service);
    }
    
    private void ThrowIfEngineMutating()
    {
        if (State is MicroEngineState.Starting or MicroEngineState.Stopping)
            throw new InvalidOperationException($"MicroEngine cannot be mutated while it is '{State}'.");
    }
    
    private void ThrowIfReentered()
    {
        if (IsWithinActiveExecutionScope())
            throw new InvalidOperationException("MicroEngine cannot be re-entered while it is executing.");
    }
    
    private bool IsWithinActiveExecutionScope() => _executionScope.Value is { IsActive: true, Depth: > 0 };
    
    private void EnterReentrancyScope()
    {
        ExecutionScopeState? scope = _executionScope.Value;
        if (scope is null || !scope.IsActive)
        {
            scope = new ExecutionScopeState();
            _executionScope.Value = scope;
        }
        
        scope.Depth++;
    }
    
    private bool TryEnterExecutionScope() => _executionGate.Wait(0);
    
    private ValueTask<bool> TryEnterExecutionScopeAsync(CancellationToken cancellationToken) => new(TryEnterExecutionScopeAsyncCore(cancellationToken));
    
    private async Task<bool> TryEnterExecutionScopeAsyncCore(CancellationToken cancellationToken) => await _executionGate.WaitAsync(0, cancellationToken);
    
    private async ValueTask EnterExecutionScopeAsync(CancellationToken cancellationToken) => await _executionGate.WaitAsync(cancellationToken);
    
    private void EnterExecutionScope() => _executionGate.Wait();
    
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
        }
        
        _executionGate.Release();
    }
    
    internal void DetachDisposedObject(MicroObject microObject)
    {
        lock (_gate)
        {
            _objects.Remove(microObject);
        }
        
        microObject.DetachFromEngine(this);
    }
    
    internal void DisposeObject(MicroObject microObject)
    {
        ArgumentNullException.ThrowIfNull(microObject);
        
        if (IsWithinActiveExecutionScope())
            throw new InvalidOperationException("MicroEngine cannot be mutated while it is executing.");
        
        bool gateAcquired;
        lock (_gate)
        {
            gateAcquired = State == MicroEngineState.Starting;
        }
        
        if (gateAcquired)
        {
            EnterExecutionScope();
        }
        else if (!TryEnterExecutionScope())
        {
            throw new InvalidOperationException("MicroEngine cannot be mutated while it is executing.");
        }
        
        EnterReentrancyScope();
        try
        {
            bool wasRegistered;
            Exception? disposalException = null;
            
            lock (_gate)
            {
                wasRegistered = _objects.Contains(microObject);
            }
            
            try
            {
                microObject.DisposeCore();
            }
            catch (Exception ex)
            {
                disposalException = ex;
            }
            
            if (wasRegistered && microObject.State == MicroObjectState.Disposed)
            {
                lock (_gate)
                {
                    _objects.Remove(microObject);
                }
                
                microObject.DetachFromEngine(this);
            }
            
            if (disposalException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(disposalException).Throw();
            }
        }
        finally
        {
            ExitExecutionScope();
        }
    }
    
    // 记录当前异步流进入执行作用域的嵌套深度，用于检测可重入调用。
    private sealed class ExecutionScopeState
    {
        public int Depth { get; set; }
        
        public bool IsActive { get; set; } = true;
    }
    
    internal void MarkFaulted()
    {
        lock (_gate)
        {
            if (State == MicroEngineState.Running)
                State = MicroEngineState.Faulted;
        }
    }
}