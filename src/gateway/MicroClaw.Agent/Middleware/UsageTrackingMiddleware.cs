using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.Middleware;

/// <summary>
/// 用量追踪中间件（逻辑从 <c>AgentRunner.TrackUsageAsync()</c> 提取为独立静态类）。
/// 从 <see cref="AgentResponseUpdate"/> 流中捕获最后一个非空的 <see cref="UsageDetails"/>，并在 Agent 运行完成后调用 <see cref="IUsageTracker"/>。
/// </summary>
public static class UsageTrackingMiddleware
{
    /// <summary>从流式更新中提取最后一次 Usage 信息并存入捕获器。</summary>
    public static UsageCapture CreateCapture() => new();

    /// <summary>异步追踪并持久化 Token 用量；若 usage 为 null 或 token 数为 0 则静默跳过。</summary>
    public static async Task TrackAsync(
        UsageCapture capture,
        string? sessionId,
        ProviderConfig provider,
        string source,
        IUsageTracker tracker,
        Microsoft.Extensions.Logging.ILogger logger,
        string? agentId = null,
        decimal? monthlyBudgetUsd = null,
        CancellationToken ct = default)
    {
        if (capture.LastUsage is null) return;
        UsageDetails usage = capture.LastUsage;
        long inputTokens = usage.InputTokenCount ?? 0L;
        long outputTokens = usage.OutputTokenCount ?? 0L;
        if (inputTokens <= 0 && outputTokens <= 0) return;

        long cachedInputTokens = usage.CachedInputTokenCount ?? 0L;
        long nonCachedInput = inputTokens - cachedInputTokens;

        decimal inputCost = nonCachedInput > 0 && provider.Capabilities.InputPricePerMToken.HasValue
            ? nonCachedInput * provider.Capabilities.InputPricePerMToken.Value / 1_000_000m : 0m;
        decimal outputCost = provider.Capabilities.OutputPricePerMToken.HasValue
            ? outputTokens * provider.Capabilities.OutputPricePerMToken.Value / 1_000_000m : 0m;
        decimal cacheInputCost = cachedInputTokens > 0
            ? cachedInputTokens * (provider.Capabilities.CacheInputPricePerMToken ?? provider.Capabilities.InputPricePerMToken ?? 0m) / 1_000_000m : 0m;

        try
        {
            await tracker.TrackAsync(
                sessionId, provider.Id, provider.DisplayName, source,
                inputTokens, outputTokens, cachedInputTokens,
                inputCost, outputCost, cacheInputCost, cacheOutputCostUsd: 0m,
                agentId: agentId, monthlyBudgetUsd: monthlyBudgetUsd, ct: ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to track token usage for session {SessionId}", sessionId);
        }
    }
}

/// <summary>线程安全（volatile 写/读）的 <see cref="UsageDetails"/> 捕获器。</summary>
public sealed class UsageCapture
{
    private volatile UsageDetails? _last;
    public UsageDetails? LastUsage
    {
        get => _last;
        set => _last = value;
    }
}
