namespace MicroClaw.Core;

public enum MicroServiceState
{
    Stopped,
    Starting,
    Running,
    Stopping,
}

public abstract class MicroService
{
    public virtual int Order => 0;

    public MicroEngine? Engine { get; private set; }

    public MicroServiceState State { get; private set; } = MicroServiceState.Stopped;

    public bool IsStarted => State == MicroServiceState.Running;

    internal void AttachToEngine(MicroEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);

        if (Engine is not null && !ReferenceEquals(Engine, engine))
            throw new InvalidOperationException("A MicroService can only belong to one MicroEngine at a time.");

        Engine = engine;
    }

    internal void DetachFromEngine(MicroEngine engine)
    {
        if (ReferenceEquals(Engine, engine))
            Engine = null;
    }

    internal async ValueTask StartInternalAsync(CancellationToken cancellationToken = default)
    {
        if (State == MicroServiceState.Running)
            return;

        if (State != MicroServiceState.Stopped)
            throw new InvalidOperationException($"MicroService cannot start while it is '{State}'.");

        State = MicroServiceState.Starting;
        await StartAsync(cancellationToken);
        State = MicroServiceState.Running;
    }

    internal async ValueTask StopInternalAsync(CancellationToken cancellationToken = default)
    {
        if (State == MicroServiceState.Stopped || State == MicroServiceState.Stopping)
            return;

        MicroServiceState previousState = State;
        State = MicroServiceState.Stopping;

        try
        {
            await StopAsync(cancellationToken);
            State = MicroServiceState.Stopped;
        }
        catch
        {
            State = previousState;
            throw;
        }
    }

    protected virtual ValueTask StartAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;

    protected virtual ValueTask StopAsync(CancellationToken cancellationToken = default)
        => ValueTask.CompletedTask;
}