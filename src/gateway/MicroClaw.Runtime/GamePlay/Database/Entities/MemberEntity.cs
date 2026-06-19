using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 成员（弟子 / 侠客）：气血、精力、盘缠、血脉。对应 people 页 / sects 编制表。
/// </summary>
[Table("member")]
public class MemberEntity : IDatabaseEntity
{
    /// <summary>主键，稳定领域 Id。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Indexed]
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>姓名（陆小寒 / 阿七）。</summary>
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>性别：男 / 女。</summary>
    [Column("gender")]
    public string? Gender { get; set; }

    /// <summary>类别：Disciple 弟子 / Hero 侠客。</summary>
    [Column("kind")]
    public string Kind { get; set; } = "Disciple";

    /// <summary>所属门派（侠客可空）。</summary>
    [Indexed]
    [Column("faction_id")]
    public string? FactionId { get; set; }

    /// <summary>状态：Active / AtRisk / Retired / Dead。</summary>
    [Column("status")]
    public string Status { get; set; } = "Active";

    /// <summary>当前气血。</summary>
    [Column("hp")]
    public int Hp { get; set; }

    /// <summary>气血上限。</summary>
    [Column("hp_max")]
    public int HpMax { get; set; }

    /// <summary>当前精力。</summary>
    [Column("stamina")]
    public int Stamina { get; set; }

    /// <summary>精力上限。</summary>
    [Column("stamina_max")]
    public int StaminaMax { get; set; }

    /// <summary>盘缠（两）。</summary>
    [Column("purse")]
    public long Purse { get; set; }

    /// <summary>每周期进食消耗（两）。</summary>
    [Column("food_cost_per_cycle")]
    public long FoodCostPerCycle { get; set; }

    /// <summary>正在执行的悬赏（可空）。</summary>
    [Indexed]
    [Column("current_bounty_id")]
    public string? CurrentBountyId { get; set; }

    /// <summary>血脉·父（可空）。</summary>
    [Indexed]
    [Column("father_id")]
    public string? FatherId { get; set; }

    /// <summary>血脉·母（可空）。</summary>
    [Indexed]
    [Column("mother_id")]
    public string? MotherId { get; set; }

    /// <summary>松耦合指向真实 Agent（可空）。</summary>
    [Indexed]
    [Column("backing_agent_id")]
    public string? BackingAgentId { get; set; }

    /// <summary>最后更新时间（Unix 毫秒）。</summary>
    [Column("updated_at_ms")]
    public long UpdatedAtMs { get; set; }
}
