using Microsoft.AspNetCore.SignalR;

namespace MicroClaw.Gateway.Hub.Hubs;

public class GatewayHub : Microsoft.AspNetCore.SignalR.Hub
{
    public async Task Ping(string message)
    {
        await Clients.Caller.SendAsync("pong", new
        {
            message,
            utcNow = DateTimeOffset.UtcNow
        });
    }
}