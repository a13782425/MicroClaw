using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 聚贤庄候选：后代 / 侠客 / 亡者回响（Echo）。含血脉双亲与继承功法。对应 recruitment-hall 页。
/// </summary>
[Table("candidate")]
public class CandidateEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>候选类别：Descendant 后代 / Hero 侠客 / Echo 回响。</summary>
    [Indexed]
    [Column("category")]
    public string Category { get; set; } = "Descendant";

    /// <summary>姓名（苏砚）。</summary>
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>性别。</summary>
    [Column("gender")]
    public string? Gender { get; set; }

    /// <summary>出生窍数。</summary>
    [Column("born_slot_count")]
    public int BornSlotCount { get; set; }

    /// <summary>招募费（两）。</summary>
    [Column("recruitment_fee")]
    public long RecruitmentFee { get; set; }

    /// <summary>继承功法 productId 列表（JSON）。</summary>
    [Column("inherited_gongfa_json")]
    public string? InheritedGongfaJson { get; set; }

    /// <summary>血脉·父（可空）。</summary>
    [Indexed]
    [Column("father_id")]
    public string? FatherId { get; set; }

    /// <summary>血脉·母（可空）。</summary>
    [Indexed]
    [Column("mother_id")]
    public string? MotherId { get; set; }

    /// <summary>Echo 回响的亡者 member_id（仅 Echo 类别）。</summary>
    [Column("source_member_id")]
    public string? SourceMemberId { get; set; }

    /// <summary>招募后生成的 member.id（可空 = 未招募）。</summary>
    [Column("recruited_member_id")]
    public string? RecruitedMemberId { get; set; }

    /// <summary>状态：Pool 候选 / Hired 已招募。</summary>
    [Column("status")]
    public string Status { get; set; } = "Pool";

    /// <summary>入池时间（Unix 毫秒）。</summary>
    [Column("created_at_ms")]
    public long CreatedAtMs { get; set; }
}
