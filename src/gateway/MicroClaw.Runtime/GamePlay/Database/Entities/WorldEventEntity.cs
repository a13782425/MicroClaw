using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 世界事件流（append-only）。各领域服务状态变更时写一条，喂 overview 的 timeline 与分类计数。
/// </summary>
[Table("world_event")]
public class WorldEventEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>事件类别：Adjudication 裁决 / Bounty 悬赏 / Contract 长约 / Ecology 生态 / Character 人物 / Recruitment 聚贤庄。</summary>
    [Indexed("ix_event_category_cycle", 1)]
    [Column("category")]
    public string Category { get; set; } = "Bounty";

    /// <summary>事件标题（【裁决】悬赏「搭建购物网站」已完成）。</summary>
    [Column("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>严重度（决定 timeline 圆点颜色）：Success / Info / Warning / Danger。</summary>
    [Column("severity")]
    public string Severity { get; set; } = "Info";

    /// <summary>关联业务类型（可空）。</summary>
    [Column("ref_type")]
    public string? RefType { get; set; }

    /// <summary>关联业务 Id（可空）。</summary>
    [Indexed]
    [Column("ref_id")]
    public string? RefId { get; set; }

    /// <summary>发生周期。</summary>
    [Indexed("ix_event_category_cycle", 2)]
    [Column("cycle_number")]
    public int CycleNumber { get; set; }

    /// <summary>发生时间（Unix 毫秒）。</summary>
    [Column("created_at_ms")]
    public long CreatedAtMs { get; set; }
}
