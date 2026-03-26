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
    int? ContextWindowMessages = null,
    /// <summary>
    /// 是否通过 A2A（Agent-to-Agent）协议对外暴露此 Agent。
    /// 启用后将在 /a2a/agent/{id} 提供符合 A2A 规范的 JSON-RPC 端点。
    /// </summary>
    bool ExposeAsA2A = false);
