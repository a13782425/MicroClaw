namespace MicroClaw.Core;

public abstract class MicroUpdateService : MicroService
{
    public abstract ValueTask TickAsync(TimeSpan deltaTime, CancellationToken cancellationToken = default);
}