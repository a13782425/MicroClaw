
namespace MicroClaw.Configuration.Options;

/// <summary>
/// Agent 运行行为配置 + Agent 实体列表。
/// 通过 <c>agents.yaml</c> 持久化，通过 <see cref="MicroClawConfig.Get{T}"/> 读取，
/// 通过 <see cref="MicroClawConfig.Save{T}"/> 写回。
/// </summary>
[MicroClawYamlConfig("agents", FileName = "agents.yaml", IsWritable = true)]
public sealed class AgentsOptions : IMicroClawConfigTemplate
{
    [YamlMember(Alias = "sub_agent_max_depth")]
    public int SubAgentMaxDepth { get; set; } = 3;

    [YamlMember(Alias = "items")]
    public List<AgentConfigEntity> Items { get; set; } = [];

    public IMicroClawConfigOptions CreateDefaultTemplate() => new AgentsOptions();
}
