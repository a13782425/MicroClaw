namespace MicroClaw.Core;

public enum MicroEngineState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted,
}

public sealed class MicroEngine
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _executionGate = new(1, 1);
    private readonly IServiceProvider _serviceProvider;
    private readonly List<MicroObject> _objects = [];
    private readonly List<MicroService> _services = [];

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
            foreach (MicroService service in attachedServices.AsEnumerable().Reverse())
            {
                _services.Remove(service);
                service.DetachFromEngine(this);
            }

            throw;
        }
    }

    public MicroEngineState State { get; private set; } = MicroEngineState.Stopped;

    public bool IsStarted => State == MicroEngineState.Running;

    public T? GetService<T>() where T : class
        => _serviceProvider.GetService(typeof(T)) as T;

    public T GetRequiredService<T>() where T : class
        => GetService<T>() ?? throw new InvalidOperationException($"Service '{typeof(T).FullName}' is not registered.");

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

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        MicroService[] serviceSnapshot;
        MicroObject[] objectSnapshot;

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

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        MicroService[] serviceSnapshot;
        MicroObject[] objectSnapshot;

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

        List<Exception> errors = [];
        bool gateAcquired = false;

        try
        {
            try
            {
                await _executionGate.WaitAsync(cancellationToken);
                gateAcquired = true;
            }
            catch (OperationCanceledException ex)
            {
                errors.Add(ex);
            }

            if (gateAcquired)
            {
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
        }
        finally
        {
            if (gateAcquired)
                _executionGate.Release();

            lock (_gate)
            {
                State = errors.Count == 0 ? MicroEngineState.Stopped : MicroEngineState.Faulted;
            }
        }

        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();

        if (errors.Count > 1)
            throw new AggregateException(errors);
    }

    public async ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default)
    {
        if (deltaTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(deltaTime), "Tick delta time must be non-negative.");

        await _executionGate.WaitAsync(cancellationToken);
        try
        {
            MicroUpdateService[] updates;
            lock (_gate)
            {
                if (State != MicroEngineState.Running)
                    throw new InvalidOperationException("MicroEngine must be started before ticking.");

                updates = _services.OfType<MicroUpdateService>()
                    .Where(static service => service.IsStarted)
                    .OrderBy(static service => service.Order)
                    .ToArray();
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
            _executionGate.Release();
        }
    }

    public bool RegisterObject(MicroObject microObject)
    {
        ArgumentNullException.ThrowIfNull(microObject);

        if (!TryEnterExecutionScope())
            throw new InvalidOperationException("MicroEngine cannot be mutated while it is executing.");

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
            _executionGate.Release();
        }
    }

    public bool UnregisterObject(MicroObject microObject)
    {
        ArgumentNullException.ThrowIfNull(microObject);

        if (!TryEnterExecutionScope())
            throw new InvalidOperationException("MicroEngine cannot be mutated while it is executing.");

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
            _executionGate.Release();
        }
    }

    public async ValueTask<bool> RegisterServiceAsync(MicroService service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);

        if (!await TryEnterExecutionScopeAsync(cancellationToken))
            throw new InvalidOperationException("MicroEngine cannot be mutated while it is executing.");

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
            _executionGate.Release();
        }
    }

    public async ValueTask<bool> UnregisterServiceAsync(MicroService service, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);

        if (!await TryEnterExecutionScopeAsync(cancellationToken))
            throw new InvalidOperationException("MicroEngine cannot be mutated while it is executing.");

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
            _executionGate.Release();
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

    private bool TryEnterExecutionScope()
        => _executionGate.Wait(0);

    private ValueTask<bool> TryEnterExecutionScopeAsync(CancellationToken cancellationToken)
        => new(_executionGate.WaitAsync(0, cancellationToken));

    internal void DetachDisposedObject(MicroObject microObject)
    {
        lock (_gate)
        {
            _objects.Remove(microObject);
        }

        microObject.DetachFromEngine(this);
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