using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 长约触发记录。长约按作息表到点贴榜时产生一行，记录每次触发的承接/代班/质检/赏金。
/// 对应 bounty-board 长约触发记录表。
/// </summary>
[Table("contract_trigger_record")]
public class ContractTriggerRecordEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>长约悬赏 Id。</summary>
    [Indexed]
    [Column("bounty_id")]
    public string BountyId { get; set; } = string.Empty;

    /// <summary>触发周期。</summary>
    [Indexed]
    [Column("trigger_cycle")]
    public int TriggerCycle { get; set; }

    /// <summary>本次承接方。</summary>
    [Column("acceptor_id")]
    public string? AcceptorId { get; set; }

    /// <summary>是否侠客代班。</summary>
    [Column("is_proxy")]
    public bool IsProxy { get; set; }

    /// <summary>盟主质检结果（通过 / 未质检）。</summary>
    [Column("lord_qc")]
    public string? LordQc { get; set; }

    /// <summary>主公抽检结果（待周结 / 未抽）。</summary>
    [Column("lord_spot")]
    public string? LordSpot { get; set; }

    /// <summary>本次赏金（两）。</summary>
    [Column("reward")]
    public long Reward { get; set; }
}
