using System.Text.Json;
using MicroClaw.Agent.Tools;
using MicroClaw.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 配置的 CRUD 存储，基于 EF Core（与 ChannelConfigStore 结构对称）。
/// </summary>
public sealed class AgentStore(IDbContextFactory<GatewayDbContext> factory)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyList<AgentConfig> All
    {
        get
        {
            using GatewayDbContext db = factory.CreateDbContext();
            return db.Agents.Select(e => ToConfig(e)).ToList().AsReadOnly();
        }
    }

    public AgentConfig? GetById(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        AgentConfigEntity? entity = db.Agents.Find(id);
        return entity is null ? null : ToConfig(entity);
    }

    public AgentConfig Add(AgentConfig config)
    {
        AgentConfigEntity entity = ToEntity(config with { Id = Guid.NewGuid().ToString("N") });
        using GatewayDbContext db = factory.CreateDbContext();
        db.Agents.Add(entity);
        db.SaveChanges();
        return ToConfig(entity);
    }

    public AgentConfig? Update(string id, AgentConfig incoming)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        AgentConfigEntity? entity = db.Agents.Find(id);
        if (entity is null) return null;

        entity.Name = incoming.Name;
        entity.SystemPrompt = incoming.SystemPrompt;
        entity.ProviderId = incoming.ProviderId;
        entity.IsEnabled = incoming.IsEnabled;
        entity.BoundChannelIdsJson = incoming.BoundChannelIds.Count > 0
            ? JsonSerializer.Serialize(incoming.BoundChannelIds, JsonOpts)
            : null;
        entity.McpServersJson = incoming.McpServers.Count > 0
            ? JsonSerializer.Serialize(incoming.McpServers, JsonOpts)
            : null;

        db.SaveChanges();
        return ToConfig(entity);
    }

    public bool Delete(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        AgentConfigEntity? entity = db.Agents.Find(id);
        if (entity is null) return false;

        db.Agents.Remove(entity);
        db.SaveChanges();
        return true;
    }

    private static AgentConfig ToConfig(AgentConfigEntity e) => new(
        e.Id,
        e.Name,
        e.SystemPrompt,
        e.ProviderId,
        e.IsEnabled,
        DeserializeList<string>(e.BoundChannelIdsJson),
        DeserializeList<McpServerConfig>(e.McpServersJson),
        e.CreatedAtUtc);

    private static AgentConfigEntity ToEntity(AgentConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        SystemPrompt = c.SystemPrompt,
        ProviderId = c.ProviderId,
        IsEnabled = c.IsEnabled,
        BoundChannelIdsJson = c.BoundChannelIds.Count > 0
            ? JsonSerializer.Serialize(c.BoundChannelIds, JsonOpts)
            : null,
        McpServersJson = c.McpServers.Count > 0
            ? JsonSerializer.Serialize(c.McpServers, JsonOpts)
            : null,
        CreatedAtUtc = c.CreatedAtUtc,
    };

    private static IReadOnlyList<T> DeserializeList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<T[]>(json, JsonOpts) ?? [];
    }
}
