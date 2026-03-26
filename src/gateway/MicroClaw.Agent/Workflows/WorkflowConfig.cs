namespace MicroClaw.Agent.Workflows;

/// <summary>工作流定义（领域模型，对应 workflows 表）。</summary>
public sealed record WorkflowConfig(
    string Id,
    string Name,
    string Description,
    bool IsEnabled,
    IReadOnlyList<WorkflowNodeConfig> Nodes,
    IReadOnlyList<WorkflowEdgeConfig> Edges,
    string? EntryNodeId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>工作流节点（对应 Agent、函数、路由等执行单元）。</summary>
public sealed record WorkflowNodeConfig(
    string NodeId,
    string Label,
    WorkflowNodeType Type,
    string? AgentId,
    string? FunctionName,
    IReadOnlyDictionary<string, string>? Config,
    WorkflowPosition? Position);

/// <summary>工作流有向边（描述节点间数据流向及条件路由）。</summary>
public sealed record WorkflowEdgeConfig(
    string SourceNodeId,
    string TargetNodeId,
    string? Condition,
    string? Label);

/// <summary>工作流节点类型。</summary>
public enum WorkflowNodeType
{
    Agent,
    Function,
    Router,
    Start,
    End
}

/// <summary>画布坐标（前端布局用）。</summary>
public sealed record WorkflowPosition(double X, double Y);
