using System.Text.Json;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Plugins;
using MicroClaw.Agent.Memory;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Infrastructure;
using MicroClaw.Providers;
using MicroClaw.Tools;
using MicroClaw.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 配置的 CRUD 存储，基于 MicroClawConfig AgentsOptions（内存 + 写时落盘到 agents.yaml）。
/// </summary>
public sealed class AgentStore : IPluginAgentRegistrar, IAgentRepository, IService
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

    // ── IService ─────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public int InitOrder => 10;

    /// <summary>确保默认 Agent（main）存在，并初始化其 DNA 目录。</summary>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        AgentConfig main = EnsureMainAgent();
        var agentDna = _sp.GetRequiredService<AgentDnaService>();
        agentDna.InitializeAgent(main.Id);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── Queries ─────────────────────────────────────────────────────────

    public IReadOnlyList<AgentConfig> All
    {
        get
        {
            _lock.EnterReadLock();
            try { return GetItems().Select(ToConfig).ToList().AsReadOnly(); }
            finally { _lock.ExitReadLock(); }
        }
    }

    public AgentConfig? GetById(string id)
    {
        _lock.EnterReadLock();
        try { return GetItems().FirstOrDefault(e => e.Id == id) is { } e ? ToConfig(e) : null; }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>返回 IsDefault=true 的代理，不存在时返回 null。</summary>
    public AgentConfig? GetDefault()
    {
        _lock.EnterReadLock();
        try { return GetItems().FirstOrDefault(e => e.IsDefault) is { } e ? ToConfig(e) : null; }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>按名称查找已启用的 Agent。</summary>
    public AgentConfig? GetByName(string name)
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems().FirstOrDefault(e => e.Name == name && e.IsEnabled) is { } e
                ? ToConfig(e)
                : null;
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// 确保存在默认代理（main）。幂等，多次调用不会创建重复记录。
    /// </summary>
    public AgentConfig EnsureMainAgent()
    {
        AgentConfig? existing = GetDefault();
        if (existing is not null) return existing;

        return Add(new AgentConfig(
            Id: string.Empty,
            Name: "main",
            Description: string.Empty,
            IsEnabled: true,
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow,
            IsDefault: true));
    }

    // ── Commands ─────────────────────────────────────────────────────────

    public AgentConfig Add(AgentConfig config)
    {
        var entity = ToEntity(config with { Id = Guid.NewGuid().ToString("N") });

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

        return ToConfig(entity);
    }

    public AgentConfig? Update(string id, AgentConfig incoming)
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
            return ToConfig(updated);
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>更新 Agent 禁用的 MCP Server ID 排除列表。</summary>
    public AgentConfig? UpdateDisabledMcpServerIds(string id, IReadOnlyList<string> mcpServerIds)
        => MutateItem(id, e => e with
        {
            DisabledMcpServerIdsJson = mcpServerIds.Count > 0
                ? JsonSerializer.Serialize(mcpServerIds, JsonOpts) : null
        });

    /// <summary>更新 Agent 的工具分组启用配置。</summary>
    public AgentConfig? UpdateToolGroupConfigs(string id, IReadOnlyList<ToolGroupConfig> configs)
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

    private AgentConfig? MutateItem(string id, Func<AgentConfigEntity, AgentConfigEntity> mutate)
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
            return ToConfig(mutated);
        }
        finally { _lock.ExitWriteLock(); }
    }

    private static AgentConfig ToConfig(AgentConfigEntity e) => new(
        e.Id,
        e.Name,
        e.Description,
        e.IsEnabled,
        DeserializeList<string>(e.DisabledSkillIdsJson),
        DeserializeList<string>(e.DisabledMcpServerIdsJson),
        DeserializeList<ToolGroupConfig>(e.ToolGroupConfigsJson),
        TimeUtils.FromMs(e.CreatedAtMs),
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

    private static Agent ToAgent(AgentConfigEntity e) =>
        Agent.Reconstitute(
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

    // ── Agent 实体查询（返回领域对象）──────────────────────────────────────

    /// <summary>按 ID 查找并返回 Agent 领域对象，不存在时返回 null。</summary>
    public Agent? GetAgentById(string id)
    {
        _lock.EnterReadLock();
        try { return GetItems().FirstOrDefault(e => e.Id == id) is { } e ? ToAgent(e) : null; }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>返回 IsDefault=true 的 Agent 领域对象，不存在时返回 null。</summary>
    public Agent? GetDefaultAgent()
    {
        _lock.EnterReadLock();
        try { return GetItems().FirstOrDefault(e => e.IsDefault) is { } e ? ToAgent(e) : null; }
        finally { _lock.ExitReadLock(); }
    }

    // ── IAgentRepository 显式接口实现 ─────────────────────────────────────

    IReadOnlyList<Agent> IAgentRepository.GetAll()
    {
        _lock.EnterReadLock();
        try { return GetItems().Select(ToAgent).ToList().AsReadOnly(); }
        finally { _lock.ExitReadLock(); }
    }

    Agent? IAgentRepository.GetById(string id) => GetAgentById(id);

    Agent? IAgentRepository.GetDefault() => GetDefaultAgent();

    Agent? IAgentRepository.GetByName(string name)
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems().FirstOrDefault(e => e.Name == name && e.IsEnabled) is { } e
                ? ToAgent(e)
                : null;
        }
        finally { _lock.ExitReadLock(); }
    }

    Agent IAgentRepository.Save(Agent agent)
    {
        if (string.IsNullOrEmpty(agent.Id))
        {
            // 新建：通过 AgentConfig.Create 流程分配 ID
            AgentConfig created = Add(agent.ToConfig());
            _lock.EnterReadLock();
            try
            {
                AgentConfigEntity entity = GetItems().First(e => e.Id == created.Id);
                return ToAgent(entity);
            }
            finally { _lock.ExitReadLock(); }
        }
        else
        {
            // 更新
            AgentConfig? updated = Update(agent.Id, agent.ToConfig());
            if (updated is null) throw new KeyNotFoundException($"Agent '{agent.Id}' not found.");
            _lock.EnterReadLock();
            try
            {
                AgentConfigEntity entity = GetItems().First(e => e.Id == agent.Id);
                return ToAgent(entity);
            }
            finally { _lock.ExitReadLock(); }
        }
    }

    bool IAgentRepository.Delete(string id) => Delete(id);
}
