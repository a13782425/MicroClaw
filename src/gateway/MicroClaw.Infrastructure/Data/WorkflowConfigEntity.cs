namespace MicroClaw.Infrastructure.Data;

/// <summary>工作流配置实体（对应 workflows 表）。</summary>
public sealed class WorkflowConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    /// <summary>JSON 序列化的 WorkflowNodeConfig 列表。</summary>
    public string? NodesJson { get; set; }
    /// <summary>JSON 序列化的 WorkflowEdgeConfig 列表。</summary>
    public string? EdgesJson { get; set; }
    public string? EntryNodeId { get; set; }
    public long CreatedAtMs { get; set; }
    public long UpdatedAtMs { get; set; }
}
