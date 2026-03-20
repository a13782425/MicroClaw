using System.Text.Json;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Tools;
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

    /// <summary>返回 IsDefault=true 的代理（系统默认代理 main），不存在时返回 null。</summary>
    public AgentConfig? GetDefault()
    {
        using GatewayDbContext db = factory.CreateDbContext();
        AgentConfigEntity? entity = db.Agents.FirstOrDefault(a => a.IsDefault);
        return entity is null ? null : ToConfig(entity);
    }

    /// <summary>
    /// 确保存在默认代理（main）。若已有 IsDefault=true 的代理则直接返回，否则创建。
    /// 幂等：多次调用不会创建重复记录。
    /// </summary>
    public AgentConfig EnsureMainAgent()
    {
        AgentConfig? existing = GetDefault();
        if (existing is not null) return existing;

        AgentConfig main = new(
            Id: string.Empty,
            Name: "main",
            SystemPrompt: string.Empty,
            IsEnabled: true,
            BoundSkillIds: [],
            McpServers: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow,
            IsDefault: true);

        return Add(main);
    }

    /// <summary>更新 Agent 的工具分组启用配置。</summary>
    public AgentConfig? UpdateToolGroupConfigs(string id, IReadOnlyList<ToolGroupConfig> configs)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        AgentConfigEntity? entity = db.Agents.Find(id);
        if (entity is null) return null;

        entity.ToolGroupConfigsJson = configs.Count > 0
            ? JsonSerializer.Serialize(configs, JsonOpts)
            : null;
        db.SaveChanges();
        return ToConfig(entity);
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

        // 默认代理的名称受保护，不允许修改
        if (!entity.IsDefault)
            entity.Name = incoming.Name;

        entity.SystemPrompt = incoming.SystemPrompt;
        entity.IsEnabled = incoming.IsEnabled;
        entity.BoundSkillIdsJson = incoming.BoundSkillIds.Count > 0
            ? JsonSerializer.Serialize(incoming.BoundSkillIds, JsonOpts)
            : null;
        entity.McpServersJson = incoming.McpServers.Count > 0
            ? JsonSerializer.Serialize(incoming.McpServers, JsonOpts)
            : null;
        entity.ToolGroupConfigsJson = incoming.ToolGroupConfigs.Count > 0
            ? JsonSerializer.Serialize(incoming.ToolGroupConfigs, JsonOpts)
            : null;

        db.SaveChanges();
        return ToConfig(entity);
    }

    /// <summary>删除代理。若代理为默认代理（IsDefault=true）则拒绝删除并返回 false。</summary>
    public bool Delete(string id)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        AgentConfigEntity? entity = db.Agents.Find(id);
        if (entity is null) return false;
        if (entity.IsDefault) return false;

        db.Agents.Remove(entity);
        db.SaveChanges();
        return true;
    }

    private static AgentConfig ToConfig(AgentConfigEntity e) => new(
        e.Id,
        e.Name,
        e.SystemPrompt,
        e.IsEnabled,
        DeserializeList<string>(e.BoundSkillIdsJson),
        DeserializeList<McpServerConfig>(e.McpServersJson),
        DeserializeList<ToolGroupConfig>(e.ToolGroupConfigsJson),
        e.CreatedAtUtc,
        e.IsDefault);

    private static AgentConfigEntity ToEntity(AgentConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        SystemPrompt = c.SystemPrompt,
        IsEnabled = c.IsEnabled,
        BoundSkillIdsJson = c.BoundSkillIds.Count > 0
            ? JsonSerializer.Serialize(c.BoundSkillIds, JsonOpts)
            : null,
        McpServersJson = c.McpServers.Count > 0
            ? JsonSerializer.Serialize(c.McpServers, JsonOpts)
            : null,
        ToolGroupConfigsJson = c.ToolGroupConfigs.Count > 0
            ? JsonSerializer.Serialize(c.ToolGroupConfigs, JsonOpts)
            : null,
        CreatedAtUtc = c.CreatedAtUtc,
        IsDefault = c.IsDefault,
    };

    private static IReadOnlyList<T> DeserializeList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<T[]>(json, JsonOpts) ?? [];
    }
}
