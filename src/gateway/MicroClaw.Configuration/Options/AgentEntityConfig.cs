using YamlDotNet.Serialization;

namespace MicroClaw.Configuration.Options;

/// <summary>
/// 单个 Agent 的持久化配置实体。
/// </summary>
public sealed record AgentEntityConfig
{
    /// <summary>
    /// Agent 的唯一标识。
    /// </summary>
    [YamlMember(Alias = "id", Description = "Agent 的唯一标识。")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Agent 的展示名称。
    /// </summary>
    [YamlMember(Alias = "name", Description = "Agent 的展示名称。")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Agent 的功能描述，用于说明职责和用途。
    /// </summary>
    [YamlMember(Alias = "description", Description = "Agent 的功能描述，用于说明职责和用途。")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 指示 Agent 当前是否启用。
    /// </summary>
    [YamlMember(Alias = "is_enabled", Description = "指示 Agent 当前是否启用。")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 被禁用的技能 ID 列表，使用 JSON 字符串持久化。
    /// </summary>
    [YamlMember(Alias = "disabled_skill_ids_json", Description = "被禁用的技能 ID 列表，使用 JSON 字符串持久化。")]
    public string DisabledSkillIdsJson { get; set; } = string.Empty;

    /// <summary>
    /// 被禁用的 MCP 服务器 ID 列表，使用 JSON 字符串持久化。
    /// </summary>
    [YamlMember(Alias = "disabled_mcp_server_ids_json", Description = "被禁用的 MCP 服务器 ID 列表，使用 JSON 字符串持久化。")]
    public string DisabledMcpServerIdsJson { get; set; } = string.Empty;

    /// <summary>
    /// 工具分组配置，使用 JSON 字符串持久化。
    /// </summary>
    [YamlMember(Alias = "tool_group_configs_json", Description = "工具分组配置，使用 JSON 字符串持久化。")]
    public string ToolGroupConfigsJson { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间的 Unix 毫秒时间戳。
    /// </summary>
    [YamlMember(Alias = "created_at_ms", Description = "创建时间的 Unix 毫秒时间戳。")]
    public long CreatedAtMs { get; set; }

    /// <summary>
    /// 指示该 Agent 是否为系统默认 Agent。
    /// </summary>
    [YamlMember(Alias = "is_default", Description = "指示该 Agent 是否为系统默认 Agent。")]
    public bool IsDefault { get; set; }
    
    /// <summary>
    /// 上下文窗口中保留的消息条数上限。
    /// </summary>
    [YamlMember(Alias = "context_window_messages", Description = "上下文窗口中保留的消息条数上限。")]
    public int ContextWindowMessages { get; set; } = 10;

    /// <summary>
    /// 允许调用的子 Agent ID 列表，使用 JSON 字符串持久化。
    /// </summary>
    [YamlMember(Alias = "allowed_sub_agent_ids_json", Description = "允许调用的子 Agent ID 列表，使用 JSON 字符串持久化。")]
    public string AllowedSubAgentIdsJson { get; set; } = string.Empty;

    /// <summary>
    /// Provider 路由策略名称。
    /// </summary>
    [YamlMember(Alias = "routing_strategy", Description = "Provider 路由策略名称。")]
    public string RoutingStrategy { get; set; } = string.Empty;
    
    /// <summary>
    /// 每月预算上限，单位为美元。
    /// </summary>
    [YamlMember(Alias = "monthly_budget_usd", Description = "每月预算上限，单位为美元。")]
    public decimal MonthlyBudgetUsd { get; set; } = -1;

    /// <summary>
    /// 来源插件 ID，用于标识该 Agent 是否由插件注入。
    /// </summary>
    [YamlMember(Alias = "source_plugin", Description = "来源插件 ID，用于标识该 Agent 是否由插件注入。")]
    public string SourcePlugin { get; set; } = string.Empty;
}
