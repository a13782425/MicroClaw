using MicroClaw.Providers;
using MicroClaw.Tools;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 配置数据模型。Agent 作为能力组合器，默认启用所有 MCP Server 和技能。
/// 通过 DisabledSkillIds / DisabledMcpServerIds 排除不需要的资源（opt-out 模型）。
/// </summary>
public sealed record AgentConfig(
    string Id,
    string Name,
    string Description,
    bool IsEnabled,
    IReadOnlyList<string> DisabledSkillIds,
    IReadOnlyList<string> DisabledMcpServerIds,
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
    bool ExposeAsA2A = false,
    /// <summary>
    /// 允许调用的子代理 ID 白名单。
    /// null = 允许调用所有子代理（默认）；空列表 = 禁止调用任何子代理；具体 ID 列表 = 仅允许调用指定子代理。
    /// </summary>
    IReadOnlyList<string>? AllowedSubAgentIds = null,
    /// <summary>
    /// Provider 路由策略：当 Session 未绑定具体 Provider 时，按此策略从已启用的 Provider 中自动选择。
    /// 默认为 <see cref="ProviderRoutingStrategy.Default"/>（使用标记为默认的 Provider）。
    /// </summary>
    ProviderRoutingStrategy RoutingStrategy = ProviderRoutingStrategy.Default,
    /// <summary>
    /// 月度预算上限（USD）。null 表示不限制。当当月累计费用达到预算 80%/100% 时记录告警日志。
    /// </summary>
    decimal? MonthlyBudgetUsd = null);
