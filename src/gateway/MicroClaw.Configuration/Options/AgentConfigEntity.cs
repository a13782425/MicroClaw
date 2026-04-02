using Microsoft.Extensions.Configuration;
using YamlDotNet.Serialization;

namespace MicroClaw.Configuration.Options;

public sealed record AgentConfigEntity
{
    [ConfigurationKeyName("id")]
    public string Id { get; set; } = string.Empty;

    [ConfigurationKeyName("name")]
    public string Name { get; set; } = string.Empty;

    [ConfigurationKeyName("description")]
    public string Description { get; set; } = string.Empty;

    [ConfigurationKeyName("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [ConfigurationKeyName("disabled_skill_ids_json")]
    public string? DisabledSkillIdsJson { get; set; }

    [ConfigurationKeyName("disabled_mcp_server_ids_json")]
    public string? DisabledMcpServerIdsJson { get; set; }

    [ConfigurationKeyName("tool_group_configs_json")]
    public string? ToolGroupConfigsJson { get; set; }

    [ConfigurationKeyName("created_at_ms")]
    public long CreatedAtMs { get; set; }

    [ConfigurationKeyName("is_default")]
    public bool IsDefault { get; set; }

    [ConfigurationKeyName("context_window_messages")]
    public int? ContextWindowMessages { get; set; }

    [ConfigurationKeyName("expose_as_a2a")]
    [YamlMember(Alias = "expose_as_a2a")]
    public bool ExposeAsA2A { get; set; }

    [ConfigurationKeyName("allowed_sub_agent_ids_json")]
    public string? AllowedSubAgentIdsJson { get; set; }

    [ConfigurationKeyName("routing_strategy")]
    public string? RoutingStrategy { get; set; }

    [ConfigurationKeyName("monthly_budget_usd")]
    public decimal? MonthlyBudgetUsd { get; set; }

    [ConfigurationKeyName("source_plugin")]
    public string? SourcePlugin { get; set; }
}
