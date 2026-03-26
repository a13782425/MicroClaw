using System.Text.Json;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Agent.Workflows;

/// <summary>
/// 工作流配置的 CRUD 存储，基于 EF Core（与 AgentStore 结构对称）。
/// </summary>
public sealed class WorkflowStore(IDbContextFactory<GatewayDbContext> factory)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyList<WorkflowConfig> All
    {
        get
        {
            using GatewayDbContext db = factory.CreateDbContext();
            return db.Workflows.Select(e => ToConfig(e)).ToList().AsReadOnly();
        }
    }

    public WorkflowConfig? GetById(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        WorkflowConfigEntity? entity = db.Workflows.Find(id);
        return entity is null ? null : ToConfig(entity);
    }

    public WorkflowConfig Add(WorkflowConfig config)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        WorkflowConfigEntity entity = ToEntity(config with { Id = Guid.NewGuid().ToString("N") });
        db.Workflows.Add(entity);
        db.SaveChanges();
        return ToConfig(entity);
    }

    public WorkflowConfig? Update(string id, WorkflowConfig config)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        WorkflowConfigEntity? entity = db.Workflows.Find(id);
        if (entity is null) return null;

        long now = TimeBase.ToMs(DateTimeOffset.UtcNow);
        entity.Name = config.Name;
        entity.Description = config.Description;
        entity.IsEnabled = config.IsEnabled;
        entity.NodesJson = config.Nodes.Count > 0 ? JsonSerializer.Serialize(config.Nodes, JsonOpts) : null;
        entity.EdgesJson = config.Edges.Count > 0 ? JsonSerializer.Serialize(config.Edges, JsonOpts) : null;
        entity.EntryNodeId = config.EntryNodeId;
        entity.UpdatedAtMs = now;

        db.SaveChanges();
        return ToConfig(entity);
    }

    public bool Delete(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        WorkflowConfigEntity? entity = db.Workflows.Find(id);
        if (entity is null) return false;
        db.Workflows.Remove(entity);
        db.SaveChanges();
        return true;
    }

    // ── 私有映射 ────────────────────────────────────────────────────────────

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
            CreatedAtMs = now,
            UpdatedAtMs = now
        };
    }
}
