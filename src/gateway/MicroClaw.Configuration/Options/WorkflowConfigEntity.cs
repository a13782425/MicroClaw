using YamlDotNet.Serialization;

namespace MicroClaw.Configuration.Options;

/// <summary>
/// 单个工作流的持久化配置实体。
/// </summary>
public sealed record WorkflowConfigEntity
{
    /// <summary>
    /// 工作流的唯一标识。
    /// </summary>
    [YamlMember(Alias = "id", Description = "工作流的唯一标识。")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 工作流的展示名称。
    /// </summary>
    [YamlMember(Alias = "name", Description = "工作流的展示名称。")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 工作流的说明描述。
    /// </summary>
    [YamlMember(Alias = "description", Description = "工作流的说明描述。")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 指示该工作流当前是否启用。
    /// </summary>
    [YamlMember(Alias = "is_enabled", Description = "指示该工作流当前是否启用。")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// JSON 序列化的工作流节点列表。
    /// </summary>
    [YamlMember(Alias = "nodes_json", Description = "JSON 序列化的工作流节点列表。")]
    public string? NodesJson { get; set; }

    /// <summary>
    /// JSON 序列化的工作流边列表。
    /// </summary>
    [YamlMember(Alias = "edges_json", Description = "JSON 序列化的工作流边列表。")]
    public string? EdgesJson { get; set; }

    /// <summary>
    /// 工作流的入口节点 ID。
    /// </summary>
    [YamlMember(Alias = "entry_node_id", Description = "工作流的入口节点 ID。")]
    public string? EntryNodeId { get; set; }

    /// <summary>
    /// 工作流默认使用的 Provider ID。
    /// </summary>
    [YamlMember(Alias = "default_provider_id", Description = "工作流默认使用的 Provider ID。")]
    public string? DefaultProviderId { get; set; }

    /// <summary>
    /// 创建时间的 Unix 毫秒时间戳。
    /// </summary>
    [YamlMember(Alias = "created_at_ms", Description = "创建时间的 Unix 毫秒时间戳。")]
    public long CreatedAtMs { get; set; }

    /// <summary>
    /// 最近更新时间的 Unix 毫秒时间戳。
    /// </summary>
    [YamlMember(Alias = "updated_at_ms", Description = "最近更新时间的 Unix 毫秒时间戳。")]
    public long UpdatedAtMs { get; set; }
}