using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 世界实例：义仓余额、当前晨钟周期、总览指标快照。每个江湖库中单行，Id 等于 WorldId。
/// </summary>
[Table("world")]
public class WorldEntity : IDatabaseEntity
{
    /// <summary>主键，等于 world_id（世界自身的 WorldId 即其 Id）。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>江湖名。</summary>
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>世界状态：Active / Paused / Archived。</summary>
    [Column("status")]
    public string Status { get; set; } = "Active";

    /// <summary>当前晨钟周期（游戏时钟，单调递增）。</summary>
    [Column("current_cycle")]
    public int CurrentCycle { get; set; }

    /// <summary>义仓余额（两）。</summary>
    [Column("treasury_balance")]
    public long TreasuryBalance { get; set; }

    /// <summary>可用余额（扣除冻结后）。</summary>
    [Column("available_balance")]
    public long AvailableBalance { get; set; }

    /// <summary>总览指标快照（弟子/侠客计数、本周期净流入等），由 tick 结算。</summary>
    [Column("metrics_json")]
    public string? MetricsJson { get; set; }

    /// <summary>最后更新时间（Unix 毫秒）。</summary>
    [Column("updated_at_ms")]
    public long UpdatedAtMs { get; set; }
}
