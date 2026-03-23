using MicroClaw.Tools;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 配置数据模型。Agent 作为能力组合器，通过引用全局 MCP Server ID 列表来指定工具。
/// </summary>
public sealed record AgentConfig(
    string Id,
    string Name,
    string Description,
    bool IsEnabled,
    IReadOnlyList<string> BoundSkillIds,
    IReadOnlyList<string> EnabledMcpServerIds,
    IReadOnlyList<ToolGroupConfig> ToolGroupConfigs,
    DateTimeOffset CreatedAtUtc,
    bool IsDefault = false,
    /// <summary>
    /// 传给 LLM 前保留的最近消息条数（含 user + assistant 轮次）。
    /// null 表示不限制，全量历史传入。建议设置为 20~100 左右。
    /// </summary>
    int? ContextWindowMessages = null);
