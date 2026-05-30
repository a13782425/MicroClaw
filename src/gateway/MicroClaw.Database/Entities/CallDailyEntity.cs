using SQLite;

namespace MicroClaw.Database;

/// <summary>
/// 每日 API 调用次数记录，按 (DayNumber, ProviderId, SessionId, Source) 聚合累加。
/// 与 TokenDaily 配对使用，分别统计"消耗了多少 Token"和"调用了多少次"。
/// </summary>
[Table("call_daily")]
public class CallDailyEntity : IDatabaseEntity
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
    [Indexed("ix_call_daily_day_provider_session_source", 1, Unique = true)]
    public int DayNumber { get; set; }

    /// <summary>
    /// 模型提供方 ID。
    /// </summary>
    [Column("provider_id")]
    [Indexed("ix_call_daily_day_provider_session_source", 2, Unique = true)]
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// 模型提供方显示名称。
    /// </summary>
    [Column("provider_name")]
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// 产生调用的 会话 ID；
    /// </summary>
    [Column("session_id")]
    [Indexed("ix_call_daily_day_provider_session_source", 3, Unique = true)]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 调用来源：chat / cron / channel / subagent。
    /// </summary>
    [Column("source")]
    [Indexed("ix_call_daily_day_provider_session_source", 4, Unique = true)]
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 当日累计调用次数（成功 + 失败）。
    /// </summary>
    [Column("call_count")]
    public long CallCount { get; set; }

    /// <summary>
    /// 当日累计成功调用次数（HTTP 2xx / 无异常）。
    /// </summary>
    [Column("success_count")]
    public long SuccessCount { get; set; }

    /// <summary>
    /// 当日累计失败调用次数（超时 / 4xx / 5xx / 异常）。
    /// </summary>
    [Column("fail_count")]
    public long FailCount { get; set; }

    /// <summary>
    /// 最后更新时间（Unix 毫秒），每次累加时更新。
    /// </summary>
    [Column("updated_at_ms")]
    public long UpdatedAtMs { get; set; }
}
