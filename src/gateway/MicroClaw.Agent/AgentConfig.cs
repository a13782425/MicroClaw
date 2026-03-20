using MicroClaw.Tools;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 配置数据模型。MCP Server 列表均序列化为 JSON 存入数据库。
/// </summary>
public sealed record AgentConfig(
    string Id,
    string Name,
    string SystemPrompt,
    bool IsEnabled,
    IReadOnlyList<string> BoundSkillIds,
    IReadOnlyList<McpServerConfig> McpServers,
    IReadOnlyList<ToolGroupConfig> ToolGroupConfigs,
    DateTimeOffset CreatedAtUtc,
    bool IsDefault = false);
