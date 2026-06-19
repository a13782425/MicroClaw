using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 功法窍装备槽。每个槽位装备一门藏经阁功法；容量由声望/品阶决定。
/// (member_id, slot_index) 唯一。
/// </summary>
[Table("member_gongfa_slot")]
public class MemberGongfaSlotEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>成员 Id。</summary>
    [Indexed("ix_gongfa_member_slot", 1, Unique = true)]
    [Column("member_id")]
    public string MemberId { get; set; } = string.Empty;

    /// <summary>槽位序号。</summary>
    [Indexed("ix_gongfa_member_slot", 2, Unique = true)]
    [Column("slot_index")]
    public int SlotIndex { get; set; }

    /// <summary>装备的藏经阁功法 productId（可空 = 空槽）。</summary>
    [Column("product_id")]
    public string? ProductId { get; set; }

    /// <summary>该功法在本槽的品阶。</summary>
    [Column("tier")]
    public string? Tier { get; set; }

    /// <summary>是否启用。</summary>
    [Column("enabled")]
    public bool Enabled { get; set; } = true;
}
