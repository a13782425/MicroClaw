using System.Collections.Concurrent;
using MicroClaw.Abstractions.Agent;
using MicroClaw.Agent.Memory;
using MicroClaw.Core;
using Microsoft.Extensions.DependencyInjection;

namespace MicroClaw.Agent;

/// <summary>
/// Agent runtime management service.
/// Manages the lifecycle of all MicroAgent instances and exposes query APIs
/// for MicroPet / SubAgentRunner / WorkflowEngine.
/// Order=10: starts before SessionService (Order=20).
/// </summary>
public sealed class MicroAgentService : MicroService, IMicroAgentService
{
    private readonly IAgentRepository _agentRepo;
    private readonly IServiceProvider _sp;

    // ── Cache ────────────────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<string, MicroAgent> _agents = new(StringComparer.Ordinal);

    public MicroAgentService(IAgentRepository agentRepo, IServiceProvider sp)
    {
        _agentRepo = agentRepo;
        _sp = sp;
    }

    // ── MicroService ─────────────────────────────────────────────────────────

    public override int Order => 10;

    /// <summary>
    /// Loads all agents from the repository, wraps them as MicroAgent instances,
    /// and initializes the DNA directory for the default agent.
    /// Mirrors the responsibility previously held by AgentStore.InitializeAsync.
    /// </summary>
    protected override async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        var agentDna = _sp.GetRequiredService<AgentDnaService>();

        foreach (AgentEntity entity in _agentRepo.GetAll())
        {
            MicroAgent agent = MicroAgent.Create(entity, _sp);
            await agent.InitializeAsync(cancellationToken);
            _agents[entity.Id] = agent;
        }

        // Initialize DNA directory for the default agent (idempotent).
        AgentEntity? main = _agentRepo.GetDefault();
        if (main is not null)
            agentDna.InitializeAgent(main.Id);
    }

    /// <summary>
    /// Disposes all cached MicroAgent instances.
    /// Collects exceptions and re-throws after all agents are disposed.
    /// </summary>
    protected override async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        MicroAgent[] snapshot = [.. _agents.Values];
        _agents.Clear();

        List<Exception> errors = [];
        foreach (MicroAgent agent in snapshot)
        {
            try
            {
                await agent.DisposeAsync();
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        if (errors.Count == 1)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(errors[0]).Throw();
        if (errors.Count > 1)
            throw new AggregateException(errors);
    }

    // ── IMicroAgentService ───────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<IMicroAgent> All => [.. _agents.Values];

    /// <inheritdoc/>
    public IMicroAgent? GetDefault() =>
        _agents.Values.FirstOrDefault(static a => a.IsDefault);

    /// <inheritdoc/>
    public IMicroAgent? GetById(string id) =>
        _agents.TryGetValue(id, out MicroAgent? agent) ? agent : null;

    // ── Cache sync (internal — called by AgentEndpoints after CRUD) ──────────

    /// <summary>
    /// Upserts the runtime cache entry for the given agent entity.
    /// Creates a new <see cref="MicroAgent"/> instance (and initializes it) if none exists;
    /// replaces the existing entry otherwise, disposing the old instance.
    /// </summary>
    internal async ValueTask<MicroAgent> RefreshAgentAsync(AgentEntity entity, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        MicroAgent fresh = MicroAgent.Create(entity, _sp);
        await fresh.InitializeAsync(ct);

        if (_agents.TryRemove(entity.Id, out MicroAgent? old))
            await old.DisposeAsync();

        _agents[entity.Id] = fresh;
        return fresh;
    }

    /// <summary>
    /// Removes the cached runtime entry for the given agent id and disposes it.
    /// No-ops silently if the id is not found.
    /// </summary>
    internal async ValueTask RemoveAgentAsync(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (_agents.TryRemove(id, out MicroAgent? agent))
            await agent.DisposeAsync();
    }
}
