using MicroClaw.Agent;
using MicroClaw.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace MicroClaw.Services;

/// <summary>
/// 通过 SignalR Hub 通知所有已连接客户端 Agent 执行状态变更。
/// </summary>
public sealed class HubAgentStatusNotifier(IHubContext<GatewayHub> hub) : IAgentStatusNotifier
{
    public Task NotifyAsync(string sessionId, string agentId, string status, CancellationToken ct = default)
        => hub.Clients.All.SendAsync("agentStatus", new { sessionId, agentId, status }, ct);
}
