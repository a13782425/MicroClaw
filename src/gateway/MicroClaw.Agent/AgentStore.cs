using System.Text.Json;
using MicroClaw.Abstractions.Plugins;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Infrastructure;
using MicroClaw.Providers;
using MicroClaw.Tools;
using MicroClaw.Utils;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 配置的 CRUD 存储，基于 MicroClawConfig AgentsOptions（内存 + 写时落盘到 agents.yaml）。
/// </summary>
public sealed class AgentStore : IPluginAgentRegistrar, IAgentRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly IServiceProvider _sp;

    public AgentStore(IServiceProvider sp) => _sp = sp;

    /// <summary>仅供测试使用的无参构造函数。</summary>
    internal AgentStore() { _sp = null!; }

    // ── Queries ─────────────────────────────────────────────────────────

    public IReadOnlyList<AgentDto> All
    {
        get
        {
            _lock.EnterReadLock();
            try { return GetItems().Select(ToDto).ToList().AsReadOnly(); }
            finally { _lock.ExitReadLock(); }
        }
    }

    public AgentDto? GetById(string id)
    {
        _lock.EnterReadLock();
        try { return GetItems().FirstOrDefault(e => e.Id == id) is { } e ? ToDto(e) : null; }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>返回 IsDefault=true 的代理，不存在时返回 null。</summary>
    public AgentDto? GetDefault()
    {
        _lock.EnterReadLock();
        try { return GetItems().FirstOrDefault(e => e.IsDefault) is { } e ? ToDto(e) : null; }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>按名称查找已启用的 Agent。</summary>
    public AgentDto? GetByName(string name)
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems().FirstOrDefault(e => e.Name == name && e.IsEnabled) is { } e
                ? ToDto(e)
                : null;
        }
        finally { _lock.ExitReadLock(); }
    }

    // ── Commands ─────────────────────────────────────────────────────────

    public AgentDto Add(AgentDto config)
    {
        var entity = ToEntity(config, Guid.NewGuid().ToString("N"));

        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<AgentsOptions>();
            if (opts.Items.Any(e => e.Name == entity.Name))
                throw new InvalidOperationException($"Agent with name '{entity.Name}' already exists.");

            MicroClawConfig.Save(new AgentsOptions
            {
                SubAgentMaxDepth = opts.SubAgentMaxDepth,
                Items = [.. opts.Items, entity]
            });
        }
        finally { _lock.ExitWriteLock(); }

        return ToDto(entity);
    }

    public AgentDto? Update(string id, AgentDto incoming)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<AgentsOptions>();
            int idx = opts.Items.FindIndex(e => e.Id == id);
            if (idx < 0) return null;

            var current = opts.Items[idx];

            if (!current.IsDefault && incoming.Name != current.Name)
            {
                if (opts.Items.Any(e => e.Name == incoming.Name && e.Id != id))
                    throw new InvalidOperationException($"Agent with name '{incoming.Name}' already exists.");
            }

            var updated = current with
            {
                Name = current.IsDefault ? current.Name : incoming.Name,
                Description = incoming.Description,
                IsEnabled = incoming.IsEnabled,
                DisabledSkillIdsJson = incoming.DisabledSkillIds.Count > 0
                    ? JsonSerializer.Serialize(incoming.DisabledSkillIds, JsonOpts) : null,
                DisabledMcpServerIdsJson = incoming.DisabledMcpServerIds.Count > 0
                    ? JsonSerializer.Serialize(incoming.DisabledMcpServerIds, JsonOpts) : null,
                ToolGroupConfigsJson = incoming.ToolGroupConfigs.Count > 0
                    ? JsonSerializer.Serialize(incoming.ToolGroupConfigs, JsonOpts) : null,
                ContextWindowMessages = incoming.ContextWindowMessages,
                ExposeAsA2A = incoming.ExposeAsA2A,
                AllowedSubAgentIdsJson = incoming.AllowedSubAgentIds is not null
                    ? JsonSerializer.Serialize(incoming.AllowedSubAgentIds, JsonOpts) : null,
                RoutingStrategy = incoming.RoutingStrategy == ProviderRoutingStrategy.Default
                    ? null : incoming.RoutingStrategy.ToString(),
                MonthlyBudgetUsd = incoming.MonthlyBudgetUsd,
            };

            var newItems = new List<AgentConfigEntity>(opts.Items) { [idx] = updated };
            MicroClawConfig.Save(new AgentsOptions { SubAgentMaxDepth = opts.SubAgentMaxDepth, Items = newItems });
            return ToDto(updated);
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>更新 Agent 禁用的 MCP Server ID 排除列表。</summary>
    public AgentDto? UpdateDisabledMcpServerIds(string id, IReadOnlyList<string> mcpServerIds)
        => MutateItem(id, e => e with
        {
            DisabledMcpServerIdsJson = mcpServerIds.Count > 0
                ? JsonSerializer.Serialize(mcpServerIds, JsonOpts) : null
        });

    /// <summary>更新 Agent 的工具分组启用配置。</summary>
    public AgentDto? UpdateToolGroupConfigs(string id, IReadOnlyList<ToolGroupConfig> configs)
        => MutateItem(id, e => e with
        {
            ToolGroupConfigsJson = configs.Count > 0
                ? JsonSerializer.Serialize(configs, JsonOpts) : null
        });

    /// <summary>删除代理。若为默认代理（IsDefault=true）则拒绝并返回 false。</summary>
    public bool Delete(string id)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<AgentsOptions>();
            var entity = opts.Items.FirstOrDefault(e => e.Id == id);
            if (entity is null || entity.IsDefault) return false;

            var newItems = opts.Items.Where(e => e.Id != id).ToList();
            MicroClawConfig.Save(new AgentsOptions { SubAgentMaxDepth = opts.SubAgentMaxDepth, Items = newItems });
            return true;
        }
        finally { _lock.ExitWriteLock(); }
    }

    // ── IPluginAgentRegistrar ────────────────────────────────────────────

    public Task ImportFromFileAsync(string filePath, string pluginName, CancellationToken ct = default)
    {
        if (!File.Exists(filePath)) return Task.CompletedTask;

        string content = File.ReadAllText(filePath);
        var (name, description) = ParseAgentFrontMatter(content, Path.GetFileNameWithoutExtension(filePath));
        string sourceTag = $"plugin:{pluginName}";

        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<AgentsOptions>();
            if (opts.Items.Any(e => e.SourcePlugin == sourceTag && e.Name == name)) return Task.CompletedTask;
            if (opts.Items.Any(e => e.Name == name)) return Task.CompletedTask;

            var entity = new AgentConfigEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Description = description,
                IsEnabled = true,
                CreatedAtMs = TimeUtils.ToMs(DateTimeOffset.UtcNow),
                SourcePlugin = sourceTag,
            };
            MicroClawConfig.Save(new AgentsOptions
            {
                SubAgentMaxDepth = opts.SubAgentMaxDepth,
                Items = [.. opts.Items, entity]
            });
        }
        finally { _lock.ExitWriteLock(); }

        return Task.CompletedTask;
    }

    public Task RemoveByPluginAsync(string pluginName, CancellationToken ct = default)
    {
        string sourceTag = $"plugin:{pluginName}";

        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<AgentsOptions>();
            var newItems = opts.Items.Where(e => e.SourcePlugin != sourceTag).ToList();
            if (newItems.Count != opts.Items.Count)
                MicroClawConfig.Save(new AgentsOptions { SubAgentMaxDepth = opts.SubAgentMaxDepth, Items = newItems });
        }
        finally { _lock.ExitWriteLock(); }

        return Task.CompletedTask;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static List<AgentConfigEntity> GetItems()
        => MicroClawConfig.Get<AgentsOptions>().Items;

    private AgentDto? MutateItem(string id, Func<AgentConfigEntity, AgentConfigEntity> mutate)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<AgentsOptions>();
            int idx = opts.Items.FindIndex(e => e.Id == id);
            if (idx < 0) return null;

            var mutated = mutate(opts.Items[idx]);
            var newItems = new List<AgentConfigEntity>(opts.Items) { [idx] = mutated };
            MicroClawConfig.Save(new AgentsOptions { SubAgentMaxDepth = opts.SubAgentMaxDepth, Items = newItems });
            return ToDto(mutated);
        }
        finally { _lock.ExitWriteLock(); }
    }

    private static AgentDto ToDto(AgentConfigEntity e) =>
        AgentDto.Reconstitute(
            id: e.Id,
            name: e.Name,
            description: e.Description,
            isEnabled: e.IsEnabled,
            disabledSkillIds: DeserializeList<string>(e.DisabledSkillIdsJson),
            disabledMcpServerIds: DeserializeList<string>(e.DisabledMcpServerIdsJson),
            toolGroupConfigs: DeserializeList<ToolGroupConfig>(e.ToolGroupConfigsJson),
            createdAtUtc: TimeUtils.FromMs(e.CreatedAtMs),
            isDefault: e.IsDefault,
            contextWindowMessages: e.ContextWindowMessages,
            exposeAsA2A: e.ExposeAsA2A,
            allowedSubAgentIds: DeserializeNullableList<string>(e.AllowedSubAgentIdsJson),
            routingStrategy: ParseRoutingStrategy(e.RoutingStrategy),
            monthlyBudgetUsd: e.MonthlyBudgetUsd);

    private static AgentConfigEntity ToEntity(AgentDto c, string? overrideId = null) => new()
    {
        Id = overrideId ?? c.Id,
        Name = c.Name,
        Description = c.Description,
        IsEnabled = c.IsEnabled,
        DisabledSkillIdsJson = c.DisabledSkillIds.Count > 0
            ? JsonSerializer.Serialize(c.DisabledSkillIds, JsonOpts) : null,
        DisabledMcpServerIdsJson = c.DisabledMcpServerIds.Count > 0
            ? JsonSerializer.Serialize(c.DisabledMcpServerIds, JsonOpts) : null,
        ToolGroupConfigsJson = c.ToolGroupConfigs.Count > 0
            ? JsonSerializer.Serialize(c.ToolGroupConfigs, JsonOpts) : null,
        CreatedAtMs = TimeUtils.ToMs(c.CreatedAtUtc),
        IsDefault = c.IsDefault,
        ContextWindowMessages = c.ContextWindowMessages,
        ExposeAsA2A = c.ExposeAsA2A,
        AllowedSubAgentIdsJson = c.AllowedSubAgentIds is not null
            ? JsonSerializer.Serialize(c.AllowedSubAgentIds, JsonOpts) : null,
        RoutingStrategy = c.RoutingStrategy == ProviderRoutingStrategy.Default
            ? null : c.RoutingStrategy.ToString(),
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
        Enum.TryParse<ProviderRoutingStrategy>(value, ignoreCase: true, out var result)
            ? result
            : ProviderRoutingStrategy.Default;

    private static (string Name, string Description) ParseAgentFrontMatter(string content, string fallbackName)
    {
        string name = fallbackName;
        string description = string.Empty;

        if (!content.StartsWith("---")) return (name, description);
        int endIdx = content.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIdx < 0) return (name, description);

        foreach (string line in content[3..endIdx].Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int colonIdx = line.IndexOf(':');
            if (colonIdx < 0) continue;
            string key = line[..colonIdx].Trim().ToLowerInvariant();
            string value = line[(colonIdx + 1)..].Trim();
            switch (key)
            {
                case "name":        if (!string.IsNullOrWhiteSpace(value)) name = value; break;
                case "description": description = value; break;
            }
        }
        return (name, description);
    }

    private static AgentDto ToAgent(AgentConfigEntity e) =>
        AgentDto.Reconstitute(
            id: e.Id,
            name: e.Name,
            description: e.Description,
            isEnabled: e.IsEnabled,
            disabledSkillIds: DeserializeList<string>(e.DisabledSkillIdsJson),
            disabledMcpServerIds: DeserializeList<string>(e.DisabledMcpServerIdsJson),
            toolGroupConfigs: DeserializeList<ToolGroupConfig>(e.ToolGroupConfigsJson),
            createdAtUtc: TimeUtils.FromMs(e.CreatedAtMs),
            isDefault: e.IsDefault,
            contextWindowMessages: e.ContextWindowMessages,
            exposeAsA2A: e.ExposeAsA2A,
            allowedSubAgentIds: DeserializeNullableList<string>(e.AllowedSubAgentIdsJson),
            routingStrategy: ParseRoutingStrategy(e.RoutingStrategy),
            monthlyBudgetUsd: e.MonthlyBudgetUsd);

    // ── IAgentRepository 显式接口实现 ─────────────────────────────────────

    IReadOnlyList<AgentDto> IAgentRepository.GetAll()
    {
        _lock.EnterReadLock();
        try { return GetItems().Select(ToDto).ToList().AsReadOnly(); }
        finally { _lock.ExitReadLock(); }
    }

    AgentDto? IAgentRepository.GetById(string id) => GetById(id);

    AgentDto? IAgentRepository.GetDefault() => GetDefault();

    AgentDto? IAgentRepository.GetByName(string name) => GetByName(name);

    AgentDto IAgentRepository.Save(AgentDto agent)
    {
        if (string.IsNullOrEmpty(agent.Id))
        {
            // 新建：Add 负责分配 ID
            return Add(agent);
        }
        else
        {
            // 更新
            AgentDto? updated = Update(agent.Id, agent);
            if (updated is null) throw new KeyNotFoundException($"Agent '{agent.Id}' not found.");
            return updated;
        }
    }

    bool IAgentRepository.Delete(string id) => Delete(id);
}
