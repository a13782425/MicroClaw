using System.Collections.Concurrent;
using MicroClaw.Abstractions.Agent;
using MicroClaw.Abstractions.Plugins;
using MicroClaw.Agent.Memory;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Core;
using MicroClaw.Infrastructure;
using MicroClaw.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace MicroClaw.Agent;
/// <summary>
/// Agent runtime management service.
/// Manages the lifecycle of all MicroAgent instances and exposes query APIs
/// for MicroPet / SubAgentRunner / WorkflowEngine.
/// Order=10: starts before SessionService (Order=20).
/// </summary>
public sealed class MicroAgentService : MicroService, IMicroAgentService, IPluginAgentRegistrar
{
    private readonly IServiceProvider _sp;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly ReaderWriterLockSlim _configLock = new(LockRecursionPolicy.NoRecursion);
    
    // ── Cache ────────────────────────────────────────────────────────────────
    
    private readonly ConcurrentDictionary<string, MicroAgent> _agents = new(StringComparer.Ordinal);
    
    public MicroAgentService(IServiceProvider sp)
    {
        _sp = sp;
    }
    
    // ── MicroService ─────────────────────────────────────────────────────────
    
    public override int Order => 10;
    
    /// <summary>
    /// Materializes persisted agents as <see cref="MicroAgent"/> instances and registers them
    /// into the owning <see cref="MicroEngine"/> before the engine start sequence begins.
    /// </summary>
    protected override async ValueTask OnAttachedAsync(CancellationToken cancellationToken = default)
    {
        await base.OnAttachedAsync(cancellationToken);
        
        MicroEngine engine = Engine ?? throw new InvalidOperationException("MicroAgentService is not attached to a MicroEngine.");
        HashSet<string> registeredAgentIds = engine.Objects.OfType<MicroAgent>().Select(static agent => agent.Id).ToHashSet(StringComparer.Ordinal);
        
        foreach (AgentEntity entity in GetPersistedAgents())
        {
            if (!registeredAgentIds.Add(entity.Id))
                continue;
            
            MicroAgent agent = new(entity, _sp);
            await engine.RegisterObjectAsync(agent, cancellationToken);
            _agents[agent.Id] = agent;
        }
    }
    
    /// <summary>
    /// Rebuilds the runtime cache from MicroEngine-owned <see cref="MicroAgent"/> instances
    /// and initializes the DNA directory for the default agent.
    /// </summary>
    protected override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        var agentDna = _sp.GetRequiredService<AgentDnaService>();
        
        _agents.Clear();
        foreach (MicroAgent agent in Engine?.Objects.OfType<MicroAgent>() ?? [])
            _agents[agent.Id] = agent;
        
        // Initialize DNA directory for the default agent (idempotent).
        IMicroAgent? main = GetDefault();
        if (main is not null)
            agentDna.InitializeAgent(main.Id);
        
        await ValueTask.CompletedTask;
    }
    
    /// <summary>
    /// Clears the runtime cache. MicroAgent lifecycle is owned by <see cref="MicroEngine"/>.
    /// </summary>
    protected override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _agents.Clear();
        await ValueTask.CompletedTask;
    }
    
    // ── IMicroAgentService ───────────────────────────────────────────────────
    
    /// <inheritdoc/>
    public IReadOnlyList<IMicroAgent> All => [.. _agents.Values];
    
    /// <inheritdoc/>
    public IMicroAgent? GetDefault() => _agents.Values.FirstOrDefault(static a => a.IsDefault);
    
    /// <inheritdoc/>
    public IMicroAgent? GetById(string id) => _agents.TryGetValue(id, out MicroAgent? agent) ? agent : null;
    
    /// <inheritdoc/>
    public IMicroAgent? GetByName(string name) => _agents.Values.FirstOrDefault(agent => agent.IsEnabled && string.Equals(agent.Name, name, StringComparison.Ordinal));
    
    /// <summary>
    /// Returns detached snapshots of all runtime agents in engine object order.
    /// </summary>
    public IReadOnlyList<AgentEntity> GetAllAgentEntities()
    {
        IEnumerable<MicroAgent> snapshot = Engine?.Objects.OfType<MicroAgent>() ?? _agents.Values.OrderBy(static agent => agent.Entity.CreatedAtUtc);
        
        return snapshot.Select(static agent => CloneAgent(agent.Entity)).ToList().AsReadOnly();
    }
    
    /// <summary>
    /// Returns a detached snapshot of the runtime agent entity by id.
    /// </summary>
    public AgentEntity? GetAgentEntityById(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return _agents.TryGetValue(id, out MicroAgent? agent) ? CloneAgent(agent.Entity) : null;
    }
    
    /// <inheritdoc/>
    public async Task ImportFromFileAsync(string filePath, string pluginName, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return;
        
        string content = File.ReadAllText(filePath);
        var (name, description) = ParseAgentFrontMatter(content, Path.GetFileNameWithoutExtension(filePath));
        string sourceTag = $"plugin:{pluginName}";
        AgentEntity? importedAgent;
        
        await _mutationGate.WaitAsync(ct);
        try
        {
            importedAgent = ImportPluginAgentCore(name, description, sourceTag);
        }
        finally
        {
            _mutationGate.Release();
        }
        
        if (importedAgent is null)
            return;
        
        try
        {
            await RefreshAgentAsync(importedAgent, ct);
        }
        catch (InvalidOperationException ex) when (IsNotAttached(ex))
        {
        }
    }
    
    /// <inheritdoc/>
    public async Task RemoveByPluginAsync(string pluginName, CancellationToken ct = default)
    {
        string sourceTag = $"plugin:{pluginName}";
        List<string> removedIds;
        
        await _mutationGate.WaitAsync(ct);
        try
        {
            removedIds = RemovePersistedAgentsByPluginCore(sourceTag);
        }
        finally
        {
            _mutationGate.Release();
        }
        
        if (removedIds.Count == 0)
            return;
        
        List<Exception> errors = [];
        foreach (string agentId in removedIds)
        {
            try
            {
                await RemoveAgentAsync(agentId);
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }
        
        if (errors.Count == 1)
            throw errors[0];
        
        if (errors.Count > 1)
            throw new AggregateException(errors);
    }
    
    // ── Cache sync (internal — called by AgentEndpoints after CRUD) ──────────
    
    /// <summary>
    /// Persists a new agent and registers its runtime <see cref="MicroAgent"/>.
    /// Rolls back the persisted entry if runtime registration fails.
    /// </summary>
    internal async ValueTask<AgentEntity> CreateAgentAsync(AgentEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        
        await _mutationGate.WaitAsync(ct);
        try
        {
            AgentEntity created = SavePersistedAgent(entity);
            try
            {
                await RefreshAgentCoreAsync(created, ct);
                return created;
            }
            catch
            {
                _ = DeletePersistedAgent(created.Id);
                throw;
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }
    
    /// <summary>
    /// Loads, mutates, persists, and applies the latest configuration to the runtime agent.
    /// Existing runtime agents are updated in place to avoid a stale-runtime replacement window.
    /// </summary>
    internal async ValueTask<AgentEntity> UpdateAgentAsync(string id, Action<AgentEntity> applyChanges, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(applyChanges);
        
        await _mutationGate.WaitAsync(ct);
        try
        {
            AgentEntity original = CloneAgent(GetPersistedAgentById(id) ?? throw new KeyNotFoundException($"Agent '{id}' not found."));
            AgentEntity working = CloneAgent(GetPersistedAgentById(id) ?? throw new KeyNotFoundException($"Agent '{id}' not found."));
            
            applyChanges(working);
            
            AgentEntity saved = SavePersistedAgent(working);
            try
            {
                await ApplyUpdatedAgentCoreAsync(saved, ct);
                return saved;
            }
            catch
            {
                SavePersistedAgent(original);
                throw;
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }
    
    /// <summary>
    /// Deletes the persisted agent and removes its runtime object.
    /// Restores the persisted entry if runtime removal fails.
    /// </summary>
    internal async ValueTask DeleteAgentAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        
        await _mutationGate.WaitAsync(ct);
        try
        {
            AgentEntity existing = CloneAgent(GetPersistedAgentById(id) ?? throw new KeyNotFoundException($"Agent '{id}' not found."));
            if (existing.IsDefault)
                throw new InvalidOperationException("Cannot delete the default agent.");
            
            if (!DeletePersistedAgent(id))
                throw new KeyNotFoundException($"Agent '{id}' not found.");
            
            try
            {
                await RemoveAgentCoreAsync(id, ct);
            }
            catch
            {
                SavePersistedAgent(existing);
                throw;
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }
    
    /// <summary>
    /// Upserts the runtime cache entry for the given agent entity.
    /// Creates a new <see cref="MicroAgent"/> instance (and initializes it) if none exists;
    /// replaces the existing entry otherwise, disposing the old instance.
    /// </summary>
    internal async ValueTask<MicroAgent> RefreshAgentAsync(AgentEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        
        await _mutationGate.WaitAsync(ct);
        try
        {
            return await RefreshAgentCoreAsync(entity, ct);
        }
        finally
        {
            _mutationGate.Release();
        }
    }
    
    private async ValueTask<MicroAgent> RefreshAgentCoreAsync(AgentEntity entity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entity);
        
        MicroEngine engine = Engine ?? throw new InvalidOperationException("MicroAgentService is not attached to a MicroEngine.");
        MicroAgent fresh = new(entity, _sp);
        await RegisterObjectWhenWritableAsync(engine, fresh, ct);
        
        if (_agents.TryGetValue(entity.Id, out MicroAgent? old))
        {
            try
            {
                await UnregisterObjectWhenWritableAsync(engine, old, ct);
            }
            catch
            {
                await RollbackRegisteredAgentAsync(engine, fresh);
                throw;
            }
            
            _agents[entity.Id] = fresh;
            await old.DisposeAsync();
        }
        else
        {
            _agents[entity.Id] = fresh;
        }
        
        return fresh;
    }
    
    private async ValueTask<MicroAgent> ApplyUpdatedAgentCoreAsync(AgentEntity entity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entity);
        
        if (_agents.TryGetValue(entity.Id, out MicroAgent? existing))
        {
            existing.ReplaceEntity(entity);
            return existing;
        }
        
        return await RefreshAgentCoreAsync(entity, ct);
    }
    
    /// <summary>
    /// Removes the cached runtime entry for the given agent id and disposes it.
    /// No-ops silently if the id is not found.
    /// </summary>
    internal async ValueTask RemoveAgentAsync(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        
        await _mutationGate.WaitAsync();
        try
        {
            await RemoveAgentCoreAsync(id, CancellationToken.None);
        }
        finally
        {
            _mutationGate.Release();
        }
    }
    
    private async ValueTask RemoveAgentCoreAsync(string id, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        
        MicroEngine engine = Engine ?? throw new InvalidOperationException("MicroAgentService is not attached to a MicroEngine.");
        if (_agents.TryGetValue(id, out MicroAgent? agent))
        {
            await UnregisterObjectWhenWritableAsync(engine, agent, ct);
            try
            {
                await agent.DisposeAsync();
                _agents.TryRemove(id, out _);
            }
            catch
            {
                await RegisterObjectWhenWritableAsync(engine, agent, ct);
                _agents[id] = agent;
                throw;
            }
        }
    }
    
    private static async ValueTask RollbackRegisteredAgentAsync(MicroEngine engine, MicroAgent agent)
    {
        try
        {
            await UnregisterObjectWhenWritableAsync(engine, agent, CancellationToken.None);
        }
        finally
        {
            await agent.DisposeAsync();
        }
    }
    
    private static async ValueTask RegisterObjectWhenWritableAsync(MicroEngine engine, MicroAgent agent, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            
            try
            {
                await engine.RegisterObjectAsync(agent, ct);
                return;
            }
            catch (InvalidOperationException ex) when (IsExecutionConflict(ex))
            {
                await Task.Yield();
            }
        }
    }
    
    private static async ValueTask UnregisterObjectWhenWritableAsync(MicroEngine engine, MicroAgent agent, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            
            try
            {
                await engine.UnregisterObjectAsync(agent, ct);
                return;
            }
            catch (InvalidOperationException ex) when (IsExecutionConflict(ex))
            {
                await Task.Yield();
            }
        }
    }
    
    private static bool IsExecutionConflict(InvalidOperationException ex) => string.Equals(ex.Message, "MicroEngine cannot be mutated while it is executing.", StringComparison.Ordinal);
    
    private static bool IsNotAttached(InvalidOperationException ex) => string.Equals(ex.Message, "MicroAgentService is not attached to a MicroEngine.", StringComparison.Ordinal);
    
    private IReadOnlyList<AgentEntity> GetPersistedAgents()
    {
        _configLock.EnterReadLock();
        try
        {
            return MicroClawConfig.Get<AgentsOptions>().Items.Select(static entity => entity.ToEntity()).ToList().AsReadOnly();
        }
        finally
        {
            _configLock.ExitReadLock();
        }
    }
    
    private AgentEntity? GetPersistedAgentById(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        
        _configLock.EnterReadLock();
        try
        {
            AgentEntityConfig? entity = MicroClawConfig.Get<AgentsOptions>().Items.FirstOrDefault(item => item.Id == id);
            return entity?.ToEntity();
        }
        finally
        {
            _configLock.ExitReadLock();
        }
    }
    
    private AgentEntity SavePersistedAgent(AgentEntity agent)
    {
        ArgumentNullException.ThrowIfNull(agent);
        
        AgentEntityConfig incoming = agent.ToConfig();
        if (string.IsNullOrWhiteSpace(incoming.Id))
            incoming = incoming with { Id = MicroClawUtils.GetUniqueId() };
        
        _configLock.EnterWriteLock();
        try
        {
            AgentsOptions options = MicroClawConfig.Get<AgentsOptions>();
            int existingIndex = options.Items.FindIndex(item => item.Id == incoming.Id);
            if (existingIndex >= 0)
            {
                AgentEntityConfig current = options.Items[existingIndex];
                if (!current.IsDefault && !string.Equals(incoming.Name, current.Name, StringComparison.Ordinal) && options.Items.Any(item => item.Id != incoming.Id && string.Equals(item.Name, incoming.Name, StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException($"Agent with name '{incoming.Name}' already exists.");
                }
                
                AgentEntityConfig updated = incoming with
                {
                    Name = current.IsDefault ? current.Name : incoming.Name, IsDefault = current.IsDefault, CreatedAtMs = current.CreatedAtMs, SourcePlugin = current.SourcePlugin,
                };
                
                List<AgentEntityConfig> newItems = new(options.Items) { [existingIndex] = updated };
                SaveAgentsOptions(options, newItems);
                return updated.ToEntity();
            }
            
            if (options.Items.Any(item => string.Equals(item.Name, incoming.Name, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Agent with name '{incoming.Name}' already exists.");
            
            SaveAgentsOptions(options, [.. options.Items, incoming]);
            return incoming.ToEntity();
        }
        finally
        {
            _configLock.ExitWriteLock();
        }
    }
    
    private bool DeletePersistedAgent(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        
        _configLock.EnterWriteLock();
        try
        {
            AgentsOptions options = MicroClawConfig.Get<AgentsOptions>();
            AgentEntityConfig? existing = options.Items.FirstOrDefault(item => item.Id == id);
            if (existing is null || existing.IsDefault)
                return false;
            
            SaveAgentsOptions(options, options.Items.Where(item => item.Id != id).ToList());
            return true;
        }
        finally
        {
            _configLock.ExitWriteLock();
        }
    }
    
    private AgentEntity? ImportPluginAgentCore(string name, string description, string sourceTag)
    {
        _configLock.EnterWriteLock();
        try
        {
            AgentsOptions options = MicroClawConfig.Get<AgentsOptions>();
            if (options.Items.Any(item => item.SourcePlugin == sourceTag && string.Equals(item.Name, name, StringComparison.Ordinal)))
                return null;
            if (options.Items.Any(item => string.Equals(item.Name, name, StringComparison.Ordinal)))
                return null;
            
            AgentEntityConfig entity = new()
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = name,
                Description = description,
                IsEnabled = true,
                CreatedAtMs = TimeUtils.ToMs(DateTimeOffset.UtcNow),
                SourcePlugin = sourceTag,
            };
            
            SaveAgentsOptions(options, [.. options.Items, entity]);
            return entity.ToEntity();
        }
        finally
        {
            _configLock.ExitWriteLock();
        }
    }
    
    private List<string> RemovePersistedAgentsByPluginCore(string sourceTag)
    {
        _configLock.EnterWriteLock();
        try
        {
            AgentsOptions options = MicroClawConfig.Get<AgentsOptions>();
            List<string> removedIds = options.Items.Where(item => item.SourcePlugin == sourceTag).Select(item => item.Id).ToList();
            if (removedIds.Count == 0)
                return removedIds;
            
            SaveAgentsOptions(options, options.Items.Where(item => item.SourcePlugin != sourceTag).ToList());
            return removedIds;
        }
        finally
        {
            _configLock.ExitWriteLock();
        }
    }
    
    private static void SaveAgentsOptions(AgentsOptions current, List<AgentEntityConfig> items)
    {
        MicroClawConfig.Save(new AgentsOptions { SubAgentMaxDepth = current.SubAgentMaxDepth, Items = items, });
    }
    
    private static (string Name, string Description) ParseAgentFrontMatter(string content, string fallbackName)
    {
        string name = fallbackName;
        string description = string.Empty;
        
        if (!content.StartsWith("---", StringComparison.Ordinal))
            return (name, description);
        
        int endIdx = content.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIdx < 0)
            return (name, description);
        
        foreach (string line in content[3..endIdx].Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int colonIdx = line.IndexOf(':');
            if (colonIdx < 0)
                continue;
            
            string key = line[..colonIdx].Trim().ToLowerInvariant();
            string value = line[(colonIdx + 1)..].Trim();
            switch (key)
            {
                case "name":
                    if (!string.IsNullOrWhiteSpace(value))
                        name = value;
                    break;
                case "description":
                    description = value;
                    break;
            }
        }
        
        return (name, description);
    }
    
    private static AgentEntity CloneAgent(AgentEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new AgentEntity(entity.Config with { });
    }
}