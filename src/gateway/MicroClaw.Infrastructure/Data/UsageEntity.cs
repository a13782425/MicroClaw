namespace MicroClaw.Infrastructure.Data;

/// <summary>
/// Token 使用量记录，按 (SessionId, ProviderId, Source, DayNumber) 每日累加。
/// 费用在写入时实时计算并累加，不再保存快照价格。
/// </summary>
public sealed class UsageEntity
{
    public int Id { get; set; }

    /// <summary>关联会话 ID（子代理调用时使用父 Agent 的 SessionId）。</summary>
    public string? SessionId { get; set; }

    public string ProviderId { get; set; } = string.Empty;
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>产生该用量的 Agent ID（空表示非 Agent 来源，如纯 cron）。</summary>
    public string? AgentId { get; set; }

    /// <summary>调用来源：chat / cron / channel / subagent</summary>
    public string Source { get; set; } = string.Empty;

    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }

    /// <summary>缓存命中的输入 token 数（已包含在 InputTokens 中，用于独立计费）。</summary>
    public long CachedInputTokens { get; set; }

    /// <summary>日期维度：相对于 TimeBase.BaseTime 的天数偏移。</summary>
    public int DayNumber { get; set; }

    /// <summary>当日累计输入费用（USD）。</summary>
    public decimal InputCostUsd { get; set; }

    /// <summary>当日累计输出费用（USD）。</summary>
    public decimal OutputCostUsd { get; set; }

    /// <summary>当日累计缓存输入费用（USD）。</summary>
    public decimal CacheInputCostUsd { get; set; }

    /// <summary>当日累计缓存输出费用（USD）。</summary>
    public decimal CacheOutputCostUsd { get; set; }

    /// <summary>首次创建时间：相对于 TimeBase.BaseTime 的毫秒偏移。</summary>
    public long CreatedAtMs { get; set; }

    /// <summary>最后更新时间：相对于 TimeBase.BaseTime 的毫秒偏移。</summary>
    public long UpdatedAtMs { get; set; }
}
