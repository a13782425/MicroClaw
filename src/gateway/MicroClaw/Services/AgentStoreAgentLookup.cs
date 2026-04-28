using MicroClaw.Agent;
using MicroClaw.Skills;

namespace MicroClaw.Services;

/// <summary>
/// 将 IAgentRepository 适配为 IAgentLookup，供 Skills 模块使用。
/// </summary>
internal sealed class AgentStoreAgentLookup(IAgentRepository agentRepo) : IAgentLookup
{
    public string? GetIdByName(string name) => agentRepo.GetByName(name)?.Id;
    public string? GetDefaultId() => agentRepo.GetDefault()?.Id;
}
