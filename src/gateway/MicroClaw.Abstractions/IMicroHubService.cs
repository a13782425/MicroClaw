namespace MicroClaw.Abstractions;

/// <summary>
/// Abstracts the SignalR hub for broadcasting real-time messages to all connected clients.
/// </summary>
public interface IMicroHubService
{
    Task SendAsync(string method, object payload, CancellationToken cancellationToken = default);
}
