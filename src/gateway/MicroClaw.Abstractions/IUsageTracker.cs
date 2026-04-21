namespace MicroClaw.Abstractions;
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