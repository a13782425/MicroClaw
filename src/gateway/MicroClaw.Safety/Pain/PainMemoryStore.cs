using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Safety;

/// <summary>
/// 基于 SQLite 的 <see cref="IPainMemoryStore"/> 实现。
/// </summary>
public sealed class PainMemoryStore : IPainMemoryStore
{
    private readonly SafetyDbContextFactory _factory;
    private readonly IPainEmotionLinker? _emotionLinker;

    public PainMemoryStore(SafetyDbContextFactory factory, IPainEmotionLinker? emotionLinker = null)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _emotionLinker = emotionLinker;
    }

    /// <inheritdoc/>
    public async Task<PainMemory> RecordAsync(PainMemory memory, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(memory);

        using var ctx = _factory.Create();

        var entity = new PainMemoryEntity
        {
            Id = memory.Id,
            AgentId = memory.AgentId,
            TriggerDescription = memory.TriggerDescription,
            ConsequenceDescription = memory.ConsequenceDescription,
            AvoidanceStrategy = memory.AvoidanceStrategy,
            Severity = memory.Severity,
            OccurrenceCount = memory.OccurrenceCount,
            LastOccurredAtMs = memory.LastOccurredAtMs,
            CreatedAtMs = memory.CreatedAtMs,
        };

        ctx.PainMemories.Add(entity);
        await ctx.SaveChangesAsync(ct);

        PainMemory saved = PainMemory.FromEntity(entity);

        // 高/极高严重度痛觉联动情绪系统（fire-and-forget 只在 linker 存在时触发）
        if (_emotionLinker is not null)
            await _emotionLinker.LinkAsync(saved, ct);

        return saved;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PainMemory>> GetAllAsync(string agentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        using var ctx = _factory.Create();

        var entities = await ctx.PainMemories
            .Where(e => e.AgentId == agentId)
            .OrderByDescending(e => e.Severity)
            .ThenByDescending(e => e.OccurrenceCount)
            .ToListAsync(ct);

        return entities.Select(PainMemory.FromEntity).ToList();
    }

    /// <inheritdoc/>
    public async Task<PainMemory?> IncrementOccurrenceAsync(
        string agentId,
        string painMemoryId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(painMemoryId);

        using var ctx = _factory.Create();

        var entity = await ctx.PainMemories
            .FirstOrDefaultAsync(e => e.Id == painMemoryId && e.AgentId == agentId, ct);

        if (entity is null)
            return null;

        entity.OccurrenceCount += 1;
        entity.LastOccurredAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await ctx.SaveChangesAsync(ct);

        return PainMemory.FromEntity(entity);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string agentId, string painMemoryId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(painMemoryId);

        using var ctx = _factory.Create();

        var entity = await ctx.PainMemories
            .FirstOrDefaultAsync(e => e.Id == painMemoryId && e.AgentId == agentId, ct);

        if (entity is null)
            return;

        ctx.PainMemories.Remove(entity);
        await ctx.SaveChangesAsync(ct);
    }
}
