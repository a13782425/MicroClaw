using MicroClaw.Abstractions;
using MicroClaw.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MicroClaw.Services;

/// <summary>
/// SignalR-backed implementation of <see cref="IMicroHubService"/>.
/// Broadcasts messages to all connected clients via <see cref="GatewayHub"/>.
/// </summary>
public sealed class MicroHubService(IHubContext<GatewayHub> hubContext) : IMicroHubService
{
    public Task SendAsync(string method, object payload, CancellationToken cancellationToken = default)
        => hubContext.Clients.All.SendAsync(method, payload, cancellationToken);
}
