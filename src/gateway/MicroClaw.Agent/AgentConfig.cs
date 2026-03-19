using MicroClaw.Agent.Tools;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 配置数据模型。MCP Server 列表和渠道绑定均序列化为 JSON 存入数据库。
/// </summary>
public sealed record AgentConfig(
    string Id,
    string Name,
    string SystemPrompt,
    string ProviderId,
    bool IsEnabled,
    IReadOnlyList<string> BoundChannelIds,
    IReadOnlyList<McpServerConfig> McpServers,
    DateTimeOffset CreatedAtUtc);
