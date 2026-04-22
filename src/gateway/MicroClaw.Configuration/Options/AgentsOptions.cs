
namespace MicroClaw.Configuration.Options;

/// <summary>
/// Agent 运行行为配置 + Agent 实体列表。
/// 通过 <c>agents.yaml</c> 持久化，通过 <see cref="MicroClawConfig.Get{T}"/> 读取，
/// 通过 <see cref="MicroClawConfig.Save{T}"/> 写回。
/// </summary>
[MicroClawYamlConfig("agents", FileName = "agents.yaml", IsWritable = true)]
public sealed class AgentsOptions : IMicroClawConfigTemplate
{
    /// <summary>
    /// 子 Agent 递归调用的最大深度，防止多级委派无限扩张。
    /// </summary>
    [YamlMember(Alias = "sub_agent_max_depth", Description = "子 Agent 递归调用的最大深度，防止多级委派无限扩张。")]
    public int SubAgentMaxDepth { get; set; } = 3;

    /// <summary>
    /// 当前系统中持久化的 Agent 配置列表。
    /// </summary>
    [YamlMember(Alias = "items", Description = "当前系统中持久化的 Agent 配置列表。")]
    public List<AgentConfigEntity> Items { get; set; } = [];

    public IMicroClawConfigOptions CreateDefaultTemplate() => new AgentsOptions();
}
