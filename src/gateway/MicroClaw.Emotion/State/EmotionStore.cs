using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Emotion;

/// <summary>
/// 基于 SQLite 的 <see cref="IEmotionStore"/> 实现。
/// 每次 <see cref="SaveAsync"/> 追加一条快照记录，历史数据不会被覆盖。
/// </summary>
public sealed class EmotionStore : IEmotionStore
{
    private readonly IDbContextFactory<GatewayDbContext> _factory;

    public EmotionStore(IDbContextFactory<GatewayDbContext> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(string agentId, EmotionState state, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(state);

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var entity = new EmotionSnapshotEntity
        {
            AgentId = agentId,
            Alertness = state.Alertness,
            Mood = state.Mood,
            Curiosity = state.Curiosity,
            Confidence = state.Confidence,
            RecordedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        ctx.EmotionSnapshots.Add(entity);
        await ctx.SaveChangesAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<EmotionState> GetCurrentAsync(string agentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var entity = await ctx.EmotionSnapshots
            .Where(e => e.AgentId == agentId)
            .OrderByDescending(e => e.RecordedAtMs)
            .FirstOrDefaultAsync(ct);

        return entity is null
            ? EmotionState.Default
            : new EmotionState(entity.Alertness, entity.Mood, entity.Curiosity, entity.Confidence);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EmotionSnapshot>> GetHistoryAsync(
        string agentId,
        long from,
        long to,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        await using var ctx = await _factory.CreateDbContextAsync(ct);

        var entities = await ctx.EmotionSnapshots
            .Where(e => e.AgentId == agentId && e.RecordedAtMs >= from && e.RecordedAtMs <= to)
            .OrderBy(e => e.RecordedAtMs)
            .ToListAsync(ct);

        return entities
            .Select(e => new EmotionSnapshot(
                new EmotionState(e.Alertness, e.Mood, e.Curiosity, e.Confidence),
                e.RecordedAtMs))
            .ToList();
    }
}
