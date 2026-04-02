using Microsoft.Extensions.Configuration;

namespace MicroClaw.Configuration.Options;

/// <summary>
/// Agent 运行行为配置 + Agent 实体列表。
/// 通过 <c>agents.yaml</c> 持久化，通过 <see cref="MicroClawConfig.Get{T}"/> 读取，
/// 通过 <see cref="MicroClawConfig.Save{T}"/> 写回。
/// </summary>
public sealed class AgentsOptions
{
    [ConfigurationKeyName("sub_agent_max_depth")]
    public int SubAgentMaxDepth { get; set; } = 3;

    [ConfigurationKeyName("items")]
    public List<AgentConfigEntity> Items { get; set; } = [];
}
