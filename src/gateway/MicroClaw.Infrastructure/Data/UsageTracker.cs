using MicroClaw.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Infrastructure.Data;

/// <summary>
/// Token 用量追踪接口，供 AgentRunner、SessionChatService 等调用以持久化每次 LLM 调用的 Token 消耗。
/// 按 (AgentId, SessionId, ProviderId, Source, DayNumber) 每日累加，费用在写入时实时计算。
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
        string? agentId = null,
        decimal? monthlyBudgetUsd = null,
        CancellationToken ct = default);
}

/// <summary>
/// 基于 EF Core 的 IUsageTracker 实现，按 (AgentId, SessionId, ProviderId, Source, DayNumber) 执行 upsert。
/// 支持月度预算告警：当月累计费用达到预算 80% 或 100% 时记录 Warning 日志。
/// </summary>
public sealed class UsageTracker(
    IDbContextFactory<GatewayDbContext> dbFactory,
    ILogger<UsageTracker> logger) : IUsageTracker
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
        string? agentId = null,
        decimal? monthlyBudgetUsd = null,
        CancellationToken ct = default)
    {
        if (inputTokens <= 0 && outputTokens <= 0) return;

        int today = TimeUtils.TodayDay();
        long nowMs = TimeUtils.NowMs();

        await using GatewayDbContext db = await dbFactory.CreateDbContextAsync(ct);

        UsageEntity? existing = await db.Usages.FirstOrDefaultAsync(
            e => e.AgentId == agentId
              && e.SessionId == sessionId
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
                AgentId = agentId,
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

        // 月度预算告警（仅当 agentId 和 monthlyBudgetUsd 均已提供时执行）
        if (!string.IsNullOrWhiteSpace(agentId) && monthlyBudgetUsd is > 0)
            await CheckBudgetAsync(db, agentId, monthlyBudgetUsd.Value, ct);
    }

    /// <summary>
    /// 计算当月该 Agent 累计费用，若超出预算阈值（80% / 100%）则记录告警日志。
    /// </summary>
    private async Task CheckBudgetAsync(
        GatewayDbContext db,
        string agentId,
        decimal budgetUsd,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        int monthStartDay = TimeUtils.ToDay(new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero));
        int todayDay = TimeUtils.TodayDay();

        decimal monthTotal = await db.Usages
            .Where(u => u.AgentId == agentId && u.DayNumber >= monthStartDay && u.DayNumber <= todayDay)
            .SumAsync(u => u.InputCostUsd + u.OutputCostUsd + u.CacheInputCostUsd + u.CacheOutputCostUsd, ct);

        double usagePct = (double)(monthTotal / budgetUsd) * 100;

        if (usagePct >= 100)
            logger.LogWarning(
                "预算超限 [{Agent}]: 月度预算 ${Budget:F4} USD，当月累计 ${Total:F4} USD（{Pct:F1}%）",
                agentId, budgetUsd, monthTotal, usagePct);
        else if (usagePct >= 80)
            logger.LogWarning(
                "预算预警 [{Agent}]: 月度预算 ${Budget:F4} USD，当月累计 ${Total:F4} USD（{Pct:F1}%），已使用 80% 以上",
                agentId, budgetUsd, monthTotal, usagePct);
    }
}
