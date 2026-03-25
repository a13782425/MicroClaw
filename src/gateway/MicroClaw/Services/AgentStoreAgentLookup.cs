using MicroClaw.Agent;
using MicroClaw.Skills;

namespace MicroClaw.Services;

/// <summary>
/// 将 AgentStore 适配为 IAgentLookup，供 Skills 模块使用。
/// </summary>
internal sealed class AgentStoreAgentLookup(AgentStore agentStore) : IAgentLookup
{
    public string? GetIdByName(string name) => agentStore.GetByName(name)?.Id;
    public string? GetDefaultId() => agentStore.GetDefault()?.Id;
}
