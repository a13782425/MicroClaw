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
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly IServiceProvider _sp;
    
    public AgentStore(IServiceProvider sp) => _sp = sp;
    
    /// <summary>仅供测试使用的无参构造函数。</summary>
    internal AgentStore()
    {
        _sp = null!;
    }
    
    // ── Queries ─────────────────────────────────────────────────────────
    
    public IReadOnlyList<AgentEntity> All
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return GetItems().Select((e) => e.ToEntity()).ToList().AsReadOnly();
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }
    
    public AgentEntity? GetById(string id)
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems().FirstOrDefault(e => e.Id == id) is { } e ? e.ToEntity() : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
    
    /// <summary>返回 IsDefault=true 的代理，不存在时返回 null。</summary>
    public AgentEntity? GetDefault()
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems().FirstOrDefault(e => e.IsDefault) is { } e ? e.ToEntity() : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
    
    /// <summary>按名称查找已启用的 Agent。</summary>
    public AgentEntity? GetByName(string name)
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems().FirstOrDefault(e => e.Name == name && e.IsEnabled) is { } e ? e.ToEntity() : null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
    
    // ── Commands ─────────────────────────────────────────────────────────
    
    public AgentEntity Add(AgentEntity config)
    {
        
        var entity = config.ToConfig();
        
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<AgentsOptions>();
            if (opts.Items.Any(e => e.Name == entity.Name))
                throw new InvalidOperationException($"Agent with name '{entity.Name}' already exists.");
            
            MicroClawConfig.Save(new AgentsOptions { SubAgentMaxDepth = opts.SubAgentMaxDepth, Items = [.. opts.Items, entity] });
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        
        return entity.ToEntity();
    }
    
    public AgentEntity? Update(string id, AgentEntity incoming)
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
                DisabledSkillIdsJson = incoming.DisabledSkillIds.Count > 0 ? JsonSerializer.Serialize(incoming.DisabledSkillIds, JsonOpts) : null,
                DisabledMcpServerIdsJson = incoming.DisabledMcpServerIds.Count > 0 ? JsonSerializer.Serialize(incoming.DisabledMcpServerIds, JsonOpts) : null,
                ToolGroupConfigsJson = incoming.ToolGroupConfigs.Count > 0 ? JsonSerializer.Serialize(incoming.ToolGroupConfigs, JsonOpts) : null,
                ContextWindowMessages = incoming.ContextWindowMessages,
                AllowedSubAgentIdsJson = incoming.AllowedSubAgentIds is not null ? JsonSerializer.Serialize(incoming.AllowedSubAgentIds, JsonOpts) : null,
                RoutingStrategy = incoming.RoutingStrategy == ProviderRoutingStrategy.Default ? null : incoming.RoutingStrategy.ToString(),
                MonthlyBudgetUsd = incoming.MonthlyBudgetUsd,
            };
            
            var newItems = new List<AgentEntityConfig>(opts.Items) { [idx] = updated };
            MicroClawConfig.Save(new AgentsOptions { SubAgentMaxDepth = opts.SubAgentMaxDepth, Items = newItems });
            return updated.ToEntity();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
    
    /// <summary>更新 Agent 禁用的 MCP Server ID 排除列表。</summary>
    public AgentEntity? UpdateDisabledMcpServerIds(string id, IReadOnlyList<string> mcpServerIds) => MutateItem(id, e => e with { DisabledMcpServerIdsJson = mcpServerIds.Count > 0 ? JsonSerializer.Serialize(mcpServerIds, JsonOpts) : null });
    
    /// <summary>更新 Agent 的工具分组启用配置。</summary>
    public AgentEntity? UpdateToolGroupConfigs(string id, IReadOnlyList<ToolGroupConfig> configs) => MutateItem(id, e => e with { ToolGroupConfigsJson = configs.Count > 0 ? JsonSerializer.Serialize(configs, JsonOpts) : null });
    
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
        finally
        {
            _lock.ExitWriteLock();
        }
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
            
            var entity = new AgentEntityConfig
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Description = description,
                IsEnabled = true,
                CreatedAtMs = TimeUtils.ToMs(DateTimeOffset.UtcNow),
                SourcePlugin = sourceTag,
            };
            MicroClawConfig.Save(new AgentsOptions { SubAgentMaxDepth = opts.SubAgentMaxDepth, Items = [.. opts.Items, entity] });
        }
        finally
        {
            _lock.ExitWriteLock();
        }
        
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
        finally
        {
            _lock.ExitWriteLock();
        }
        
        return Task.CompletedTask;
    }
    
    // ── Helpers ──────────────────────────────────────────────────────────
    
    private static List<AgentEntityConfig> GetItems() => MicroClawConfig.Get<AgentsOptions>().Items;
    
    private AgentEntity? MutateItem(string id, Func<AgentEntityConfig, AgentEntityConfig> mutate)
    {
        _lock.EnterWriteLock();
        try
        {
            var opts = MicroClawConfig.Get<AgentsOptions>();
            int idx = opts.Items.FindIndex(e => e.Id == id);
            if (idx < 0) return null;
            
            var mutated = mutate(opts.Items[idx]);
            var newItems = new List<AgentEntityConfig>(opts.Items) { [idx] = mutated };
            MicroClawConfig.Save(new AgentsOptions { SubAgentMaxDepth = opts.SubAgentMaxDepth, Items = newItems });
            return mutated.ToEntity();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

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
                case "name":
                    if (!string.IsNullOrWhiteSpace(value)) name = value;
                    break;
                case "description": description = value; break;
            }
        }
        return (name, description);
    }
    
    // ── IAgentRepository 显式接口实现 ─────────────────────────────────────
    
    IReadOnlyList<AgentEntity> IAgentRepository.GetAll()
    {
        _lock.EnterReadLock();
        try
        {
            return GetItems().Select(e=>e.ToEntity()).ToList().AsReadOnly();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
    
    AgentEntity? IAgentRepository.GetById(string id) => GetById(id);
    
    AgentEntity? IAgentRepository.GetDefault() => GetDefault();
    
    AgentEntity? IAgentRepository.GetByName(string name) => GetByName(name);
    
    AgentEntity IAgentRepository.Save(AgentEntity agent)
    {
        if (string.IsNullOrEmpty(agent.Id))
        {
            // 新建：Add 负责分配 ID
            return Add(agent);
        }
        else
        {
            // 更新
            AgentEntity? updated = Update(agent.Id, agent);
            if (updated is null) throw new KeyNotFoundException($"Agent '{agent.Id}' not found.");
            return updated;
        }
    }
    
    bool IAgentRepository.Delete(string id) => Delete(id);
}