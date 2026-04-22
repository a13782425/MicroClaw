using YamlDotNet.Serialization;

namespace MicroClaw.Configuration.Options;

public sealed record AgentConfigEntity
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = string.Empty;

    [YamlMember(Alias = "is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [YamlMember(Alias = "disabled_skill_ids_json")]
    public string? DisabledSkillIdsJson { get; set; }

    [YamlMember(Alias = "disabled_mcp_server_ids_json")]
    public string? DisabledMcpServerIdsJson { get; set; }

    [YamlMember(Alias = "tool_group_configs_json")]
    public string? ToolGroupConfigsJson { get; set; }

    [YamlMember(Alias = "created_at_ms")]
    public long CreatedAtMs { get; set; }

    [YamlMember(Alias = "is_default")]
    public bool IsDefault { get; set; }

    [YamlMember(Alias = "context_window_messages")]
    public int? ContextWindowMessages { get; set; }

    [YamlMember(Alias = "expose_as_a2a")]
    public bool ExposeAsA2A { get; set; }

    [YamlMember(Alias = "allowed_sub_agent_ids_json")]
    public string? AllowedSubAgentIdsJson { get; set; }

    [YamlMember(Alias = "routing_strategy")]
    public string? RoutingStrategy { get; set; }

    [YamlMember(Alias = "monthly_budget_usd")]
    public decimal? MonthlyBudgetUsd { get; set; }

    [YamlMember(Alias = "source_plugin")]
    public string? SourcePlugin { get; set; }
}
