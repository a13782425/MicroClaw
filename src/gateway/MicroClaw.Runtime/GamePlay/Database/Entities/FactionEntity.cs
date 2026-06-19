using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 门派：品阶、声望、编制上限、宗库余额。对应 sects 页。
/// </summary>
[Table("faction")]
public class FactionEntity : IDatabaseEntity
{
    /// <summary>主键，稳定领域 Id。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Indexed]
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>门派名（青岚门 / 墨竹门 …）。</summary>
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>品阶：Wood / Copper / Silver / Gold / Jade。</summary>
    [Column("tier")]
    public string Tier { get; set; } = "Wood";

    /// <summary>当前声望。</summary>
    [Column("reputation")]
    public long Reputation { get; set; }

    /// <summary>晋升下一阶所需声望阈值。</summary>
    [Column("next_tier_reputation")]
    public long NextTierReputation { get; set; }

    /// <summary>编制上限（弟子人数）。</summary>
    [Column("headcount_cap")]
    public int HeadcountCap { get; set; }

    /// <summary>宗库余额（两）。</summary>
    [Column("treasury_balance")]
    public long TreasuryBalance { get; set; }

    /// <summary>松耦合指向真实 Agent（A2 决策，可空）。</summary>
    [Column("backing_agent_id")]
    public string? BackingAgentId { get; set; }
}
