using Microsoft.Extensions.Configuration;

namespace MicroClaw.Configuration;

public sealed class AgentOptions
{
    /// <summary>子代理最大嵌套深度。默认 3 层。</summary>
    [ConfigurationKeyName("sub_agent_max_depth")]
    public int SubAgentMaxDepth { get; set; } = 3;
}
