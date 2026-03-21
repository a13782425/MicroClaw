namespace MicroClaw.Agent;

/// <summary>
/// 通知 Agent 执行状态变更（running / completed / failed）的接口。
/// 由主项目通过 SignalR Hub 实现，注入到 AgentRunner；状态为空实现时不发送推送。
/// </summary>
public interface IAgentStatusNotifier
{
    Task NotifyAsync(string sessionId, string agentId, string status, CancellationToken ct = default);
}
