using System;
using System.Collections.Generic;
using System.Text;

namespace MicroClaw.Database;

/// <summary>
/// 每日 Token 消耗记录，按 (DayNumber, ProviderId, SessionId, Source) 聚合累加。
/// 每次 LLM 调用后 upsert 当日对应维度的记录，费用实时计算并累加。
/// </summary>
public class TokenDailyEntity : IDatabaseEntity
{
    /// <summary>
    /// 主键，建议由 (DayNumber + ProviderId + SessionId + Source) 拼接哈希生成，确保同一天同维度唯一。
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 日期维度：相对于 TimeBase.BaseTime 的天数偏移。
    /// </summary>
    public int DayNumber { get; set; }

    /// <summary>
    /// 模型提供方 ID，对应 ProviderConfig 中的唯一标识。
    /// </summary>
    public string ProviderId { get; set; } = string.Empty;

    /// <summary>
    /// 模型提供方显示名称（如 "OpenAI / GPT-4o"）。
    /// </summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// 产生消耗的 会话 ID
    /// </summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// 调用来源
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 当日累计输入 Token 数（含缓存命中）。
    /// </summary>
    public long InputTokens { get; set; }

    /// <summary>
    /// 当日累计输出 Token 数。
    /// </summary>
    public long OutputTokens { get; set; }

    /// <summary>
    /// 当日累计缓存命中的输入 Token 数（已包含在 InputTokens 中，用于独立计费）。
    /// </summary>
    public long CachedInputTokens { get; set; }

    /// <summary>
    /// 当日累计输入费用（USD）。
    /// </summary>
    public decimal InputCostUsd { get; set; }

    /// <summary>
    /// 当日累计输出费用（USD）。
    /// </summary>
    public decimal OutputCostUsd { get; set; }

    /// <summary>
    /// 当日累计缓存输入费用（USD），缓存命中通常有折扣。
    /// </summary>
    public decimal CacheInputCostUsd { get; set; }

    /// <summary>
    /// 当日累计缓存输出费用（USD）。
    /// </summary>
    public decimal CacheOutputCostUsd { get; set; }

    /// <summary>
    /// 最后更新时间（Unix 毫秒），每次累加时更新。
    /// </summary>
    public long UpdatedAtMs { get; set; }
}