using System.Text.Json;
using MicroClaw.Abstractions.Plugins;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers;
using MicroClaw.Tools;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 配置的 CRUD 存储，基于 EF Core（与 ChannelConfigStore 结构对称）。
/// </summary>
public sealed class AgentStore(IDbContextFactory<GatewayDbContext> factory) : IPluginAgentRegistrar
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

    /// <summary>按名称查找已启用的 Agent（用于 Skills context:fork 的 agent 类型路由）。</summary>
    public AgentConfig? GetByName(string name)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        AgentConfigEntity? entity = db.Agents.FirstOrDefault(
            a => a.Name == name && a.IsEnabled);
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
            Description: string.Empty,
            IsEnabled: true,
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow,
            IsDefault: true);

        return Add(main);
    }

    /// <summary>更新 Agent 禁用的 MCP Server ID 排除列表。空列表 = 全部启用。</summary>
    public AgentConfig? UpdateDisabledMcpServerIds(string id, IReadOnlyList<string> mcpServerIds)
    {
        using GatewayDbContext db = factory.CreateDbContext();
        AgentConfigEntity? entity = db.Agents.Find(id);
        if (entity is null) return null;

        entity.DisabledMcpServerIdsJson = mcpServerIds.Count > 0
            ? JsonSerializer.Serialize(mcpServerIds, JsonOpts)
            : null;
        db.SaveChanges();
        return ToConfig(entity);
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
        using GatewayDbContext db = factory.CreateDbContext();

        // 0-B-5: 检查 Name 唯一性
        if (db.Agents.Any(a => a.Name == config.Name))
            throw new InvalidOperationException($"Agent with name '{config.Name}' already exists.");

        AgentConfigEntity entity = ToEntity(config with { Id = Guid.NewGuid().ToString("N") });
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
        {
            // 0-B-5: 名称变更时检查唯一性（排除自身）
            if (incoming.Name != entity.Name && db.Agents.Any(a => a.Name == incoming.Name && a.Id != id))
                throw new InvalidOperationException($"Agent with name '{incoming.Name}' already exists.");
            entity.Name = incoming.Name;
        }

        entity.Description = incoming.Description;
        entity.IsEnabled = incoming.IsEnabled;
        entity.DisabledSkillIdsJson = incoming.DisabledSkillIds.Count > 0
            ? JsonSerializer.Serialize(incoming.DisabledSkillIds, JsonOpts)
            : null;
        entity.DisabledMcpServerIdsJson = incoming.DisabledMcpServerIds.Count > 0
            ? JsonSerializer.Serialize(incoming.DisabledMcpServerIds, JsonOpts)
            : null;
        entity.ToolGroupConfigsJson = incoming.ToolGroupConfigs.Count > 0
            ? JsonSerializer.Serialize(incoming.ToolGroupConfigs, JsonOpts)
            : null;
        entity.ContextWindowMessages = incoming.ContextWindowMessages;
        entity.ExposeAsA2A = incoming.ExposeAsA2A;
        entity.AllowedSubAgentIdsJson = incoming.AllowedSubAgentIds is not null
            ? JsonSerializer.Serialize(incoming.AllowedSubAgentIds, JsonOpts)
            : null;
        entity.RoutingStrategy = incoming.RoutingStrategy == ProviderRoutingStrategy.Default
            ? null
            : incoming.RoutingStrategy.ToString();
        entity.MonthlyBudgetUsd = incoming.MonthlyBudgetUsd;

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
        e.Description,
        e.IsEnabled,
        DeserializeList<string>(e.DisabledSkillIdsJson),
        DeserializeList<string>(e.DisabledMcpServerIdsJson),
        DeserializeList<ToolGroupConfig>(e.ToolGroupConfigsJson),
        TimeBase.FromMs(e.CreatedAtMs),
        e.IsDefault,
        e.ContextWindowMessages,
        e.ExposeAsA2A,
        DeserializeNullableList<string>(e.AllowedSubAgentIdsJson),
        ParseRoutingStrategy(e.RoutingStrategy),
        e.MonthlyBudgetUsd);

    private static AgentConfigEntity ToEntity(AgentConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Description = c.Description,
        IsEnabled = c.IsEnabled,
        DisabledSkillIdsJson = c.DisabledSkillIds.Count > 0
            ? JsonSerializer.Serialize(c.DisabledSkillIds, JsonOpts)
            : null,
        DisabledMcpServerIdsJson = c.DisabledMcpServerIds.Count > 0
            ? JsonSerializer.Serialize(c.DisabledMcpServerIds, JsonOpts)
            : null,
        ToolGroupConfigsJson = c.ToolGroupConfigs.Count > 0
            ? JsonSerializer.Serialize(c.ToolGroupConfigs, JsonOpts)
            : null,
        CreatedAtMs = TimeBase.ToMs(c.CreatedAtUtc),
        IsDefault = c.IsDefault,
        ContextWindowMessages = c.ContextWindowMessages,
        ExposeAsA2A = c.ExposeAsA2A,
        AllowedSubAgentIdsJson = c.AllowedSubAgentIds is not null
            ? JsonSerializer.Serialize(c.AllowedSubAgentIds, JsonOpts)
            : null,
        RoutingStrategy = c.RoutingStrategy == ProviderRoutingStrategy.Default
            ? null
            : c.RoutingStrategy.ToString(),
        MonthlyBudgetUsd = c.MonthlyBudgetUsd,
    };

    private static IReadOnlyList<T> DeserializeList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<T[]>(json, JsonOpts) ?? [];
    }

    /// <summary>
    /// 反序列化 nullable 列表：null JSON 返回 null（而非空列表）。
    /// 用于区分“未设置”（null）与“明确为空”（[]）的语义。
    /// </summary>
    private static IReadOnlyList<T>? DeserializeNullableList<T>(string? json)
    {
        if (json is null) return null;
        if (string.IsNullOrWhiteSpace(json)) return [];
        return JsonSerializer.Deserialize<T[]>(json, JsonOpts) ?? [];
    }

    private static ProviderRoutingStrategy ParseRoutingStrategy(string? value) =>
        Enum.TryParse<ProviderRoutingStrategy>(value, ignoreCase: true, out ProviderRoutingStrategy result)
            ? result
            : ProviderRoutingStrategy.Default;

    // ── IPluginAgentRegistrar ───────────────────────────────────────────────

    /// <inheritdoc/>
    public Task ImportFromFileAsync(string filePath, string pluginName, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return Task.CompletedTask;

        string content = File.ReadAllText(filePath);
        var (name, description) = ParseAgentFrontMatter(content, Path.GetFileNameWithoutExtension(filePath));

        string sourceTag = $"plugin:{pluginName}";

        using GatewayDbContext db = factory.CreateDbContext();

        // Skip if agent from this plugin with same name already exists
        if (db.Agents.Any(a => a.SourcePlugin == sourceTag && a.Name == name))
            return Task.CompletedTask;

        // Also skip if a non-plugin agent with same name exists (don't overwrite user-created agents)
        if (db.Agents.Any(a => a.Name == name))
            return Task.CompletedTask;

        var entity = new AgentConfigEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Description = description,
            IsEnabled = true,
            CreatedAtMs = TimeBase.ToMs(DateTimeOffset.UtcNow),
            SourcePlugin = sourceTag,
        };
        db.Agents.Add(entity);
        db.SaveChanges();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveByPluginAsync(string pluginName, CancellationToken ct = default)
    {
        string sourceTag = $"plugin:{pluginName}";
        using GatewayDbContext db = factory.CreateDbContext();
        var toRemove = db.Agents.Where(a => a.SourcePlugin == sourceTag).ToList();
        if (toRemove.Count > 0)
        {
            db.Agents.RemoveRange(toRemove);
            db.SaveChanges();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Parse YAML front-matter from a Markdown agent definition file.
    /// Expected format:
    /// <code>
    /// ---
    /// name: agent-name
    /// description: Agent description
    /// ---
    /// </code>
    /// </summary>
    private static (string Name, string Description) ParseAgentFrontMatter(string content, string fallbackName)
    {
        string name = fallbackName;
        string description = string.Empty;

        if (!content.StartsWith("---")) return (name, description);

        int endIdx = content.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIdx < 0) return (name, description);

        string frontMatter = content[3..endIdx];
        foreach (string line in frontMatter.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int colonIdx = line.IndexOf(':');
            if (colonIdx < 0) continue;

            string key = line[..colonIdx].Trim().ToLowerInvariant();
            string value = line[(colonIdx + 1)..].Trim();

            switch (key)
            {
                case "name":
                    if (!string.IsNullOrWhiteSpace(value)) name = value;
                    break;
                case "description":
                    description = value;
                    break;
            }
        }

        return (name, description);
    }
}
