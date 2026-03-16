using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MicroClaw.Hubs;

[Authorize]
public class GatewayHub : Hub
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
