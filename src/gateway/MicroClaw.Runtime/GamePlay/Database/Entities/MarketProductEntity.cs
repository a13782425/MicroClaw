using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 三阁市集目录：藏经阁（功法）/ 天问阁（古籍）/ 百宝阁（道具）。对应 market 页。
/// </summary>
[Table("market_product")]
public class MarketProductEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>所属阁：Cangjing 藏经 / Tianwen 天问 / Baibao 百宝。</summary>
    [Indexed]
    [Column("market")]
    public string Market { get; set; } = "Cangjing";

    /// <summary>商品名（SQL 调优 / 回血丹）。</summary>
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>所属领域（功法/古籍有，道具可空）。</summary>
    [Column("domain")]
    public string? Domain { get; set; }

    /// <summary>品阶。</summary>
    [Column("tier")]
    public string? Tier { get; set; }

    /// <summary>价格（两）。</summary>
    [Column("price")]
    public long Price { get; set; }

    /// <summary>描述。</summary>
    [Column("description")]
    public string? Description { get; set; }

    /// <summary>前置领域（如 Web）。</summary>
    [Column("prereq_domain")]
    public string? PrereqDomain { get; set; }

    /// <summary>前置声望阈值（如 80）。</summary>
    [Column("prereq_value")]
    public int? PrereqValue { get; set; }

    /// <summary>时效类型：Permanent 永久 / Cycle 闭关N周期 / OneShot 一次性 / Cooldown 冷却。</summary>
    [Column("duration_type")]
    public string? DurationType { get; set; }

    /// <summary>时效数值（配合 duration_type）。</summary>
    [Column("duration_value")]
    public int? DurationValue { get; set; }
}
