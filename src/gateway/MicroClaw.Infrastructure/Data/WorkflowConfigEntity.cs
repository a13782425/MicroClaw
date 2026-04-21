using Microsoft.Extensions.Configuration;

namespace MicroClaw.Infrastructure.Data;

/// <summary>工作流配置实体（对应 workflows 表）。</summary>
public sealed class WorkflowConfigEntity
{
    [ConfigurationKeyName("id")]
    public string Id { get; set; } = string.Empty;

    [ConfigurationKeyName("name")]
    public string Name { get; set; } = string.Empty;

    [ConfigurationKeyName("description")]
    public string Description { get; set; } = string.Empty;

    [ConfigurationKeyName("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>JSON 序列化的 WorkflowNodeConfig 列表。</summary>
    [ConfigurationKeyName("nodes_json")]
    public string? NodesJson { get; set; }

    /// <summary>JSON 序列化的 WorkflowEdgeConfig 列表。</summary>
    [ConfigurationKeyName("edges_json")]
    public string? EdgesJson { get; set; }

    [ConfigurationKeyName("entry_node_id")]
    public string? EntryNodeId { get; set; }

    [ConfigurationKeyName("default_provider_id")]
    public string? DefaultProviderId { get; set; }

    [ConfigurationKeyName("created_at_ms")]
    public long CreatedAtMs { get; set; }

    [ConfigurationKeyName("updated_at_ms")]
    public long UpdatedAtMs { get; set; }
}
