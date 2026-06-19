using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 统一资金账本（append-only）。义仓 / 宗库 / 盘缠三类账户同表；balance_after 冗余便于绘图。
/// 一张表喂 sects 宗库曲线、overview 本周期净流入、market 购买流水。
/// </summary>
[Table("ledger_entry")]
public class LedgerEntryEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>账户类型：Treasury 义仓 / Faction 宗库 / Person 盘缠。</summary>
    [Indexed("ix_ledger_account_cycle", 1)]
    [Column("account_type")]
    public string AccountType { get; set; } = "Treasury";

    /// <summary>账户归属 Id（world_id / faction_id / member_id）。</summary>
    [Indexed("ix_ledger_account_cycle", 2)]
    [Column("owner_id")]
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>条目类型：Income / SalaryBurn / OpBurn / Tax / Purchase / Reward / Infusion / Food。</summary>
    [Indexed]
    [Column("entry_type")]
    public string EntryType { get; set; } = "Income";

    /// <summary>金额（正 = 流入，负 = 流出）。</summary>
    [Column("amount")]
    public long Amount { get; set; }

    /// <summary>记账后余额（冗余，免去读时重算）。</summary>
    [Column("balance_after")]
    public long BalanceAfter { get; set; }

    /// <summary>关联业务类型：Bounty / Purchase / ReputationEvent（可空）。</summary>
    [Column("ref_type")]
    public string? RefType { get; set; }

    /// <summary>关联业务 Id（可空）。</summary>
    [Indexed]
    [Column("ref_id")]
    public string? RefId { get; set; }

    /// <summary>发生周期。</summary>
    [Indexed("ix_ledger_account_cycle", 3)]
    [Column("cycle_number")]
    public int CycleNumber { get; set; }

    /// <summary>备注。</summary>
    [Column("note")]
    public string? Note { get; set; }

    /// <summary>发生时间（Unix 毫秒）。</summary>
    [Column("created_at_ms")]
    public long CreatedAtMs { get; set; }
}
