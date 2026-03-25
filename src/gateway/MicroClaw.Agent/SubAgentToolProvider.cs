using MicroClaw.Agent.Memory;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Tools;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent;

/// <summary>子代理工具提供者，包装 <see cref="SubAgentTools"/>。需要 sessionId，为空时返回空列表。</summary>
public sealed class SubAgentToolProvider(
    AgentStore agentStore,
    ISubAgentRunner subAgentRunner,
    AgentDnaService agentDnaService,
    ISessionReader sessionReader) : IBuiltinToolProvider
{
    public string GroupId => "subagent";

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        SubAgentTools.GetToolDescriptions();

    public IReadOnlyList<AIFunction> CreateTools(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return [];
        return SubAgentTools.CreateForSession(sessionId, agentStore, subAgentRunner, agentDnaService, sessionReader);
    }
}
