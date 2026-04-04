using MicroClaw.Hubs;
using MicroClaw.Pet.Heartbeat;
using Microsoft.AspNetCore.SignalR;

namespace MicroClaw.Services;

/// <summary>
/// 通过 SignalR Hub 实现 Pet 用户通知。
/// </summary>
public sealed class HubPetNotifier(IHubContext<GatewayHub> hub) : IPetNotifier
{
    public Task NotifyUserAsync(string sessionId, string message, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("petMessage", new { sessionId, message, timestamp = DateTimeOffset.UtcNow }, ct);

    public Task NotifyStateChangedAsync(string sessionId, string newState, string? reason = null, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("petStateChanged", new { sessionId, newState, reason, timestamp = DateTimeOffset.UtcNow }, ct);

    public Task NotifyActionStartedAsync(string sessionId, string actionType, string? parameter = null, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("petActionStarted", new { sessionId, actionType, parameter, timestamp = DateTimeOffset.UtcNow }, ct);

    public Task NotifyActionCompletedAsync(string sessionId, string actionType, bool succeeded, string? error = null, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("petActionCompleted", new { sessionId, actionType, succeeded, error, timestamp = DateTimeOffset.UtcNow }, ct);
}
