using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Emotion;

/// <summary>
/// 基于 SQLite 的 <see cref="IEmotionStore"/> 实现。
/// 每次 <see cref="SaveAsync"/> 追加一条快照记录，历史数据不会被覆盖。
/// </summary>
public sealed class EmotionStore : IEmotionStore
{
    private readonly EmotionDbContextFactory _factory;

    public EmotionStore(EmotionDbContextFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(string agentId, EmotionState state, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(state);

        using var ctx = _factory.Create();

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

        using var ctx = _factory.Create();

        var entity = await ctx.EmotionSnapshots
            .Where(e => e.AgentId == agentId)
            .OrderByDescending(e => e.RecordedAtMs)
            .FirstOrDefaultAsync(ct);

        return entity?.ToEmotionState() ?? EmotionState.Default;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<EmotionSnapshot>> GetHistoryAsync(
        string agentId,
        long from,
        long to,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        using var ctx = _factory.Create();

        var entities = await ctx.EmotionSnapshots
            .Where(e => e.AgentId == agentId && e.RecordedAtMs >= from && e.RecordedAtMs <= to)
            .OrderBy(e => e.RecordedAtMs)
            .ToListAsync(ct);

        return entities
            .Select(e => new EmotionSnapshot(e.ToEmotionState(), e.RecordedAtMs))
            .ToList();
    }
}
