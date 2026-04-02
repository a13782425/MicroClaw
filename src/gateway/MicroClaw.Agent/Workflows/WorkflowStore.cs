using System.Text.Json;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Agent.Workflows;

/// <summary>
/// 宸ヤ綔娴侀厤缃殑 CRUD 瀛樺偍锛屽熀浜?YAML 鏂囦欢锛堝唴瀛樼紦瀛?+ 鍐欐椂钀界洏锛夈€?
/// </summary>
public sealed class WorkflowStore(string configDir)
    : YamlFileStore<WorkflowConfigEntity>(Path.Combine(configDir, "workflows.yaml"), e => e.Id)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyList<WorkflowConfig> All
        => GetAll().Select(ToConfig).ToList().AsReadOnly();

    public WorkflowConfig? GetById(string id)
        => GetYamlById(id) is { } e ? ToConfig(e) : null;

    public WorkflowConfig Add(WorkflowConfig config)
    {
        WorkflowConfigEntity entity = ToEntity(config with { Id = Guid.NewGuid().ToString("N") });
        SetYaml(entity);
        return ToConfig(entity);
    }

    public WorkflowConfig? Update(string id, WorkflowConfig config)
    {
        long now = TimeBase.ToMs(DateTimeOffset.UtcNow);
        var updated = MutateYaml(id, e =>
        {
            e.Name = config.Name;
            e.Description = config.Description;
            e.IsEnabled = config.IsEnabled;
            e.NodesJson = config.Nodes.Count > 0 ? JsonSerializer.Serialize(config.Nodes, JsonOpts) : null;
            e.EdgesJson = config.Edges.Count > 0 ? JsonSerializer.Serialize(config.Edges, JsonOpts) : null;
            e.EntryNodeId = config.EntryNodeId;
            e.DefaultProviderId = config.DefaultProviderId;
            e.UpdatedAtMs = now;
        });
        return updated is null ? null : ToConfig(updated);
    }

    public bool Delete(string id) => RemoveYaml(id);

    // 鈹€鈹€ 绉佹湁鏄犲皠 鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€鈹€

    private static WorkflowConfig ToConfig(WorkflowConfigEntity e)
    {
        var nodes = string.IsNullOrWhiteSpace(e.NodesJson)
            ? (IReadOnlyList<WorkflowNodeConfig>)[]
            : JsonSerializer.Deserialize<List<WorkflowNodeConfig>>(e.NodesJson, JsonOpts)?.AsReadOnly()
              ?? (IReadOnlyList<WorkflowNodeConfig>)[];

        var edges = string.IsNullOrWhiteSpace(e.EdgesJson)
            ? (IReadOnlyList<WorkflowEdgeConfig>)[]
            : JsonSerializer.Deserialize<List<WorkflowEdgeConfig>>(e.EdgesJson, JsonOpts)?.AsReadOnly()
              ?? (IReadOnlyList<WorkflowEdgeConfig>)[];

        return new WorkflowConfig(
            Id: e.Id,
            Name: e.Name,
            Description: e.Description,
            IsEnabled: e.IsEnabled,
            Nodes: nodes,
            Edges: edges,
            EntryNodeId: e.EntryNodeId,
            DefaultProviderId: e.DefaultProviderId,
            CreatedAtUtc: TimeBase.FromMs(e.CreatedAtMs),
            UpdatedAtUtc: TimeBase.FromMs(e.UpdatedAtMs));
    }

    private static WorkflowConfigEntity ToEntity(WorkflowConfig c)
    {
        long now = TimeBase.ToMs(DateTimeOffset.UtcNow);
        return new WorkflowConfigEntity
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            IsEnabled = c.IsEnabled,
            NodesJson = c.Nodes.Count > 0 ? JsonSerializer.Serialize(c.Nodes, JsonOpts) : null,
            EdgesJson = c.Edges.Count > 0 ? JsonSerializer.Serialize(c.Edges, JsonOpts) : null,
            EntryNodeId = c.EntryNodeId,
            DefaultProviderId = c.DefaultProviderId,
            CreatedAtMs = now,
            UpdatedAtMs = now
        };
    }
}
