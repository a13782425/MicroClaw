using SQLite;

namespace MicroClaw.Database;

/// <summary>
/// 每日 Token 消耗记录，按 (DayNumber, ProviderId, SessionId, Source) 聚合累加。
/// 每次 LLM 调用后 upsert 当日对应维度的记录，费用实时计算并累加。
/// </summary>
[Table("token_daily")]
public class TokenDailyEntity : IDatabaseEntity
{
    /// <summary>
    /// 主键，建议由 (DayNumber + ProviderId + SessionId + Source) 拼接哈希生成，确保同一天同维度唯一。
    /// </summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 日期维度：相对于 TimeBase.BaseTime 的天数偏移。
    /// </summary>
    [Column("day_number")]
    [Indexed("ix_token_daily_day_provider_session_source", 1, Unique = true)]
    public int DayNumber { get; set; }

    /// <summary>
    /// 模型提供方 ID，对应 ProviderConfig 中的唯一标识。
    /// </summary>
    [Column("provider_id")]
    [Indexed("ix_token_daily_day_provider_session_source", 2, Unique = true)]
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// 模型提供方显示名称（如 "OpenAI / GPT-4o"）。
    /// </summary>
    [Column("provider_name")]
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// 产生消耗的 会话 ID
    /// </summary>
    [Column("session_id")]
    [Indexed("ix_token_daily_day_provider_session_source", 3, Unique = true)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 调用来源
    /// </summary>
    [Column("source")]
    [Indexed("ix_token_daily_day_provider_session_source", 4, Unique = true)]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 当日累计输入 Token 数（含缓存命中）。
    /// </summary>
    [Column("input_tokens")]
    public long InputTokens { get; set; }

    /// <summary>
    /// 当日累计输出 Token 数。
    /// </summary>
    [Column("output_tokens")]
    public long OutputTokens { get; set; }

    /// <summary>
    /// 当日累计缓存命中的输入 Token 数（已包含在 InputTokens 中，用于独立计费）。
    /// </summary>
    [Column("cached_input_tokens")]
    public long CachedInputTokens { get; set; }

    /// <summary>
    /// 当日累计输入费用（USD）。
    /// </summary>
    [Column("input_cost_usd")]
    public decimal InputCostUsd { get; set; }

    /// <summary>
    /// 当日累计输出费用（USD）。
    /// </summary>
    [Column("output_cost_usd")]
    public decimal OutputCostUsd { get; set; }

    /// <summary>
    /// 当日累计缓存输入费用（USD），缓存命中通常有折扣。
    /// </summary>
    [Column("cache_input_cost_usd")]
    public decimal CacheInputCostUsd { get; set; }

    /// <summary>
    /// 当日累计缓存输出费用（USD）。
    /// </summary>
    [Column("cache_output_cost_usd")]
    public decimal CacheOutputCostUsd { get; set; }

    /// <summary>
    /// 最后更新时间（Unix 毫秒），每次累加时更新。
    /// </summary>
    [Column("updated_at_ms")]
    public long UpdatedAtMs { get; set; }
}