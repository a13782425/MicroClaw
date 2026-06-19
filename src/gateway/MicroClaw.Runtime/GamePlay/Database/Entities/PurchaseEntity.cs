using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 市集购买流水。对应 market 页的购买动作；写入时同时产生一条 ledger_entry。
/// </summary>
[Table("purchase")]
public class PurchaseEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>买方类型：Member / Faction。</summary>
    [Indexed("ix_purchase_buyer", 1)]
    [Column("buyer_type")]
    public string BuyerType { get; set; } = "Member";

    /// <summary>买方 Id（member_id 或 faction_id）。</summary>
    [Indexed("ix_purchase_buyer", 2)]
    [Column("buyer_id")]
    public string BuyerId { get; set; } = string.Empty;

    /// <summary>商品 Id（指向 market_product）。</summary>
    [Column("product_id")]
    public string ProductId { get; set; } = string.Empty;

    /// <summary>成交价（两）。</summary>
    [Column("price")]
    public long Price { get; set; }

    /// <summary>购买周期。</summary>
    [Indexed]
    [Column("cycle_number")]
    public int CycleNumber { get; set; }

    /// <summary>对应账本条目 Id（指向 ledger_entry，可空）。</summary>
    [Column("ledger_entry_id")]
    public string? LedgerEntryId { get; set; }
}
