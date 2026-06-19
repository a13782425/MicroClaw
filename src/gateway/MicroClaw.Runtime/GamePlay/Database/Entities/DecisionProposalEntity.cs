using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 裁决方案（延期 / 撤回重发等）。盟主提出、主公确认或修改。对应 dialogue-modify 页。
/// </summary>
[Table("decision_proposal")]
public class DecisionProposalEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>方案标题（青岚门 4 张延期 + 无人接 4 张重发）。</summary>
    [Column("title")]
    public string? Title { get; set; }

    /// <summary>状态：Pending 待确认 / Confirmed 已确认 / Revised 已修改。</summary>
    [Column("status")]
    public string Status { get; set; } = "Pending";

    /// <summary>方案结构 JSON（方案一 / 方案二，内部引用 bounty）。</summary>
    [Column("plans_json")]
    public string? PlansJson { get; set; }

    /// <summary>提出周期。</summary>
    [Column("created_cycle")]
    public int CreatedCycle { get; set; }

    /// <summary>最后更新时间（Unix 毫秒）。</summary>
    [Column("updated_at_ms")]
    public long UpdatedAtMs { get; set; }
}
