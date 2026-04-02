using System.Text.Json;
using MicroClaw.Abstractions.Plugins;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers;
using MicroClaw.Tools;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 配置的 CRUD 存储，基于 YAML 文件（内存缓存 + 写时落盘）。
/// </summary>
public sealed class AgentStore(string configDir)
    : YamlFileStore<AgentConfigEntity>(Path.Combine(configDir, "agents.yaml"), e => e.Id), IPluginAgentRegistrar
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public IReadOnlyList<AgentConfig> All
        => GetAll().Select(ToConfig).ToList().AsReadOnly();

    public AgentConfig? GetById(string id)
        => GetYamlById(id) is { } e ? ToConfig(e) : null;

    /// <summary>返回 IsDefault=true 的代理（系统默认代理 main），不存在时返回 null。</summary>
    public AgentConfig? GetDefault()
        => GetAll().FirstOrDefault(e => e.IsDefault) is { } e ? ToConfig(e) : null;

    /// <summary>按名称查找已启用的 Agent（用于 Skills context:fork 的 agent 类型路由）。</summary>
    public AgentConfig? GetByName(string name)
        => GetAll().FirstOrDefault(e => e.Name == name && e.IsEnabled) is { } e ? ToConfig(e) : null;

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
        var updated = MutateYaml(id, e =>
        {
            e.DisabledMcpServerIdsJson = mcpServerIds.Count > 0
                ? JsonSerializer.Serialize(mcpServerIds, JsonOpts)
                : null;
        });
        return updated is null ? null : ToConfig(updated);
    }

    /// <summary>更新 Agent 的工具分组启用配置。</summary>
    public AgentConfig? UpdateToolGroupConfigs(string id, IReadOnlyList<ToolGroupConfig> configs)
    {
        var updated = MutateYaml(id, e =>
        {
            e.ToolGroupConfigsJson = configs.Count > 0
                ? JsonSerializer.Serialize(configs, JsonOpts)
                : null;
        });
        return updated is null ? null : ToConfig(updated);
    }

    public AgentConfig Add(AgentConfig config)
    {
        AgentConfigEntity entity = ToEntity(config with { Id = Guid.NewGuid().ToString("N") });
        ExecuteWrite(items =>
        {
            if (items.Values.Any(e => e.Name == entity.Name))
                throw new InvalidOperationException($"Agent with name '{entity.Name}' already exists.");
            items[entity.Id] = entity;
            return true;
        });
        return ToConfig(entity);
    }

    public AgentConfig? Update(string id, AgentConfig incoming)
    {
        AgentConfigEntity? current = GetYamlById(id);
        if (current is null) return null;

        // Check name uniqueness before acquiring write lock (acceptable tiny race for low-concurrency admin UI)
        if (!current.IsDefault && incoming.Name != current.Name)
        {
            if (GetAll().Any(e => e.Name == incoming.Name && e.Id != id))
                throw new InvalidOperationException($"Agent with name '{incoming.Name}' already exists.");
        }

        var updated = MutateYaml(id, e =>
        {
            if (!e.IsDefault)
                e.Name = incoming.Name;

            e.Description = incoming.Description;
            e.IsEnabled = incoming.IsEnabled;
            e.DisabledSkillIdsJson = incoming.DisabledSkillIds.Count > 0
                ? JsonSerializer.Serialize(incoming.DisabledSkillIds, JsonOpts)
                : null;
            e.DisabledMcpServerIdsJson = incoming.DisabledMcpServerIds.Count > 0
                ? JsonSerializer.Serialize(incoming.DisabledMcpServerIds, JsonOpts)
                : null;
            e.ToolGroupConfigsJson = incoming.ToolGroupConfigs.Count > 0
                ? JsonSerializer.Serialize(incoming.ToolGroupConfigs, JsonOpts)
                : null;
            e.ContextWindowMessages = incoming.ContextWindowMessages;
            e.ExposeAsA2A = incoming.ExposeAsA2A;
            e.AllowedSubAgentIdsJson = incoming.AllowedSubAgentIds is not null
                ? JsonSerializer.Serialize(incoming.AllowedSubAgentIds, JsonOpts)
                : null;
            e.RoutingStrategy = incoming.RoutingStrategy == ProviderRoutingStrategy.Default
                ? null
                : incoming.RoutingStrategy.ToString();
            e.MonthlyBudgetUsd = incoming.MonthlyBudgetUsd;
        });
        return updated is null ? null : ToConfig(updated);
    }

    /// <summary>删除代理。若代理为默认代理（IsDefault=true）则拒绝删除并返回 false。</summary>
    public bool Delete(string id)
    {
        AgentConfigEntity? entity = GetYamlById(id);
        if (entity is null || entity.IsDefault) return false;
        return RemoveYaml(id);
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

        ExecuteWrite(items =>
        {
            // Skip if agent from this plugin with same name already exists
            if (items.Values.Any(e => e.SourcePlugin == sourceTag && e.Name == name))
                return false;
            // Skip if a non-plugin agent with same name exists (don't overwrite user-created agents)
            if (items.Values.Any(e => e.Name == name))
                return false;

            AgentConfigEntity entity = new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Description = description,
                IsEnabled = true,
                CreatedAtMs = TimeBase.ToMs(DateTimeOffset.UtcNow),
                SourcePlugin = sourceTag,
            };
            items[entity.Id] = entity;
            return true;
        });
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveByPluginAsync(string pluginName, CancellationToken ct = default)
    {
        string sourceTag = $"plugin:{pluginName}";
        RemoveAllYaml(e => e.SourcePlugin == sourceTag);
        return Task.CompletedTask;
    }

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

