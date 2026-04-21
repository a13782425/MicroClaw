using System.Text.Json;
using MicroClaw.Configuration;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Utils;

namespace MicroClaw.Agent.Workflows;

/// <summary>
/// 工作流配置的 CRUD 存储，基于 MicroClaw 配置系统的 YAML 持久化。
/// </summary>
public sealed class WorkflowStore()
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly object _sync = new();

    public IReadOnlyList<WorkflowConfig> All
        => GetOptions().Items.Select(ToConfig).ToList().AsReadOnly();

    public WorkflowConfig? GetById(string id)
        => GetOptions().Items.FirstOrDefault(e => e.Id == id) is { } entity ? ToConfig(entity) : null;

    public WorkflowConfig Add(WorkflowConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        lock (_sync)
        {
            WorkflowsOptions options = GetOptions();
            long now = TimeUtils.NowMs();
            WorkflowConfigEntity entity = ToEntity(config with
            {
                Id = Guid.NewGuid().ToString("N"),
                CreatedAtUtc = TimeUtils.FromMs(now),
                UpdatedAtUtc = TimeUtils.FromMs(now)
            });

            SaveOptions(new WorkflowsOptions
            {
                Items = [.. options.Items, entity]
            });

            return ToConfig(entity);
        }
    }

    public WorkflowConfig? Update(string id, WorkflowConfig config)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(config);

        lock (_sync)
        {
            WorkflowsOptions options = GetOptions();
            WorkflowConfigEntity? existing = options.Items.FirstOrDefault(e => e.Id == id);
            if (existing is null)
                return null;

            long now = TimeUtils.NowMs();
            WorkflowConfigEntity updated = ToEntity(config with
            {
                Id = existing.Id,
                CreatedAtUtc = TimeUtils.FromMs(existing.CreatedAtMs),
                UpdatedAtUtc = TimeUtils.FromMs(now)
            });

            SaveOptions(new WorkflowsOptions
            {
                Items = options.Items.Select(item => item.Id == id ? updated : item).ToList()
            });

            return ToConfig(updated);
        }
    }

    public bool Delete(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        lock (_sync)
        {
            WorkflowsOptions options = GetOptions();
            List<WorkflowConfigEntity> remaining = options.Items.Where(item => item.Id != id).ToList();
            if (remaining.Count == options.Items.Count)
                return false;

            SaveOptions(new WorkflowsOptions { Items = remaining });
            return true;
        }
    }

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
            CreatedAtUtc: TimeUtils.FromMs(e.CreatedAtMs),
            UpdatedAtUtc: TimeUtils.FromMs(e.UpdatedAtMs));
    }

    private static WorkflowConfigEntity ToEntity(WorkflowConfig c)
    {
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
            CreatedAtMs = TimeUtils.ToMs(c.CreatedAtUtc),
            UpdatedAtMs = TimeUtils.ToMs(c.UpdatedAtUtc)
        };
    }

    private static WorkflowsOptions GetOptions() => MicroClawConfig.Get<WorkflowsOptions>();

    private static void SaveOptions(WorkflowsOptions options) => MicroClawConfig.Save(options);
}
