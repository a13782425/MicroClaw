using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 功法 / 古籍 / 道具所有权（门派 or 个人统一表）。sects 古籍表、people 个人古籍、candidate 继承古籍都查本表。
/// </summary>
[Table("ownership")]
public class OwnershipEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>持有方类型：Member / Faction。</summary>
    [Indexed("ix_own_owner", 1)]
    [Column("owner_type")]
    public string OwnerType { get; set; } = "Member";

    /// <summary>持有方 Id（member_id 或 faction_id）。</summary>
    [Indexed("ix_own_owner", 2)]
    [Column("owner_id")]
    public string OwnerId { get; set; } = string.Empty;

    /// <summary>商品 Id（指向 market_product）。</summary>
    [Indexed]
    [Column("product_id")]
    public string ProductId { get; set; } = string.Empty;

    /// <summary>状态：Readable 可读 / Locked 闭关中 / Consumed 已消耗。</summary>
    [Column("status")]
    public string Status { get; set; } = "Readable";

    /// <summary>获得周期。</summary>
    [Column("acquired_cycle")]
    public int AcquiredCycle { get; set; }

    /// <summary>继承来源：Father / Mother / Purchased（可空）。</summary>
    [Column("origin")]
    public string? Origin { get; set; }

    /// <summary>时效到期周期（可空 = 永久）。</summary>
    [Column("expires_cycle")]
    public int? ExpiresCycle { get; set; }
}
