using MicroClaw.Abstractions.Agent;
using MicroClaw.Skills;

namespace MicroClaw.Services;

/// <summary>
/// 将 IMicroAgentService 适配为 IAgentLookup，供 Skills 模块使用。
/// </summary>
internal sealed class MicroAgentLookup(IMicroAgentService agentService) : IAgentLookup
{
    public string? GetIdByName(string name) => agentService.GetByName(name)?.Id;
    public string? GetDefaultId() => agentService.GetDefault()?.Id;
}