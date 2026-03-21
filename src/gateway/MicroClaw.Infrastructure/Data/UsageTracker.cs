using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Infrastructure.Data;

/// <summary>
/// Token 用量追踪接口，供 AgentRunner、SessionChatService 等调用以持久化每次 LLM 调用的 Token 消耗。
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
        decimal? inputPricePerMToken,
        decimal? outputPricePerMToken,
        CancellationToken ct = default);
}

/// <summary>
/// 基于 EF Core 的 IUsageTracker 实现，将使用记录写入 usages 表。
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
        decimal? inputPricePerMToken,
        decimal? outputPricePerMToken,
        CancellationToken ct = default)
    {
        if (inputTokens <= 0 && outputTokens <= 0) return;

        await using GatewayDbContext db = await dbFactory.CreateDbContextAsync(ct);
        db.Usages.Add(new UsageEntity
        {
            SessionId = sessionId,
            ProviderId = providerId,
            ProviderName = providerName,
            Source = source,
            InputTokens = inputTokens,
            OutputTokens = outputTokens,
            InputPricePerMToken = inputPricePerMToken,
            OutputPricePerMToken = outputPricePerMToken,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
    }
}
