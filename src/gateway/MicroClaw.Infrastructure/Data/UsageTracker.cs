using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Infrastructure.Data;

/// <summary>
/// Token 用量追踪接口，供 AgentRunner、SessionChatService 等调用以持久化每次 LLM 调用的 Token 消耗。
/// 按 (SessionId, ProviderId, Source, DayNumber) 每日累加，费用在写入时实时计算。
/// </summary>
public interface IUsageTracker
{
    Task TrackAsync(
        string? sessionId,
        string providerId,
        string providerName,
        string source,
        long inputTokens,
        long outputTokens,
        long cachedInputTokens = 0,
        decimal inputCostUsd = 0m,
        decimal outputCostUsd = 0m,
        decimal cacheInputCostUsd = 0m,
        decimal cacheOutputCostUsd = 0m,
        CancellationToken ct = default);
}

/// <summary>
/// 基于 EF Core 的 IUsageTracker 实现，按 (SessionId, ProviderId, Source, DayNumber) 执行 upsert。
/// </summary>
public sealed class UsageTracker(IDbContextFactory<GatewayDbContext> dbFactory) : IUsageTracker
{
    public async Task TrackAsync(
        string? sessionId,
        string providerId,
        string providerName,
        string source,
        long inputTokens,
        long outputTokens,
        long cachedInputTokens = 0,
        decimal inputCostUsd = 0m,
        decimal outputCostUsd = 0m,
        decimal cacheInputCostUsd = 0m,
        decimal cacheOutputCostUsd = 0m,
        CancellationToken ct = default)
    {
        if (inputTokens <= 0 && outputTokens <= 0) return;

        int today = TimeBase.TodayDay();
        long nowMs = TimeBase.NowMs();

        await using GatewayDbContext db = await dbFactory.CreateDbContextAsync(ct);

        UsageEntity? existing = await db.Usages.FirstOrDefaultAsync(
            e => e.SessionId == sessionId
              && e.ProviderId == providerId
              && e.Source == source
              && e.DayNumber == today, ct);

        if (existing is not null)
        {
            existing.InputTokens += inputTokens;
            existing.OutputTokens += outputTokens;
            existing.CachedInputTokens += cachedInputTokens;
            existing.InputCostUsd += inputCostUsd;
            existing.OutputCostUsd += outputCostUsd;
            existing.CacheInputCostUsd += cacheInputCostUsd;
            existing.CacheOutputCostUsd += cacheOutputCostUsd;
            existing.UpdatedAtMs = nowMs;
        }
        else
        {
            db.Usages.Add(new UsageEntity
            {
                SessionId = sessionId,
                ProviderId = providerId,
                ProviderName = providerName,
                Source = source,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CachedInputTokens = cachedInputTokens,
                DayNumber = today,
                InputCostUsd = inputCostUsd,
                OutputCostUsd = outputCostUsd,
                CacheInputCostUsd = cacheInputCostUsd,
                CacheOutputCostUsd = cacheOutputCostUsd,
                CreatedAtMs = nowMs,
                UpdatedAtMs = nowMs,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
