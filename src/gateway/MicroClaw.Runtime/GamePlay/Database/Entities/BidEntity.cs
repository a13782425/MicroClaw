using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 竞价卡。一个悬赏下多家门派竞价，(bounty_id, faction_id) 唯一。对应 bounty-board 竞价表格。
/// </summary>
[Table("bid")]
public class BidEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>悬赏 Id。</summary>
    [Indexed("ix_bid_bounty_faction", 1, Unique = true)]
    [Column("bounty_id")]
    public string BountyId { get; set; } = string.Empty;

    /// <summary>竞价门派 Id。</summary>
    [Indexed("ix_bid_bounty_faction", 2, Unique = true)]
    [Column("faction_id")]
    public string FactionId { get; set; } = string.Empty;

    /// <summary>报价（两）。</summary>
    [Column("price")]
    public long Price { get; set; }

    /// <summary>打法（前后端分治 / 模板快速搭建）。</summary>
    [Column("approach")]
    public string? Approach { get; set; }

    /// <summary>阵容与能力（JSON）。</summary>
    [Column("lineup_json")]
    public string? LineupJson { get; set; }

    /// <summary>声望。</summary>
    [Column("reputation")]
    public int? Reputation { get; set; }

    /// <summary>档期（天）。</summary>
    [Column("schedule_days")]
    public int? ScheduleDays { get; set; }

    /// <summary>是否被裁决人选定。</summary>
    [Column("is_selected")]
    public bool IsSelected { get; set; }

    /// <summary>出价时间（Unix 毫秒）。</summary>
    [Column("created_at_ms")]
    public long CreatedAtMs { get; set; }
}
