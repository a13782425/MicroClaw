using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 悬赏状态机核心。含普通悬赏与长约；5 个状态：竞价/执行中/长约/待确认/无人接。
/// 对应 bounty-board 页。
/// </summary>
[Table("bounty")]
public class BountyEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Indexed("ix_bounty_status", 1)]
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>标题（搭建购物网站）。</summary>
    [Column("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>类型：Bounty 悬赏 / Contract 长约。</summary>
    [Column("kind")]
    public string Kind { get; set; } = "Bounty";

    /// <summary>状态：Bidding / Executing / Contract / Confirmation / Unclaimed / Done。</summary>
    [Indexed("ix_bounty_status", 2)]
    [Column("status")]
    public string Status { get; set; } = "Bidding";

    /// <summary>发起人（主公 / 飞书驿）。</summary>
    [Column("initiator")]
    public string? Initiator { get; set; }

    /// <summary>裁决人。</summary>
    [Column("adjudicator")]
    public string? Adjudicator { get; set; }

    /// <summary>基础赏金（两）。</summary>
    [Column("base_reward")]
    public long BaseReward { get; set; }

    /// <summary>质量系数（×10，18 = 1.8）。</summary>
    [Column("quality_factor")]
    public int QualityFactor { get; set; }

    /// <summary>难度系数（×10）。</summary>
    [Column("difficulty_factor")]
    public int DifficultyFactor { get; set; }

    /// <summary>来源驿。</summary>
    [Column("source_station")]
    public string? SourceStation { get; set; }

    /// <summary>截止周期（可空）。</summary>
    [Column("deadline_cycle")]
    public int? DeadlineCycle { get; set; }

    /// <summary>长约窗口起（如 09:00）。</summary>
    [Column("window_start")]
    public string? WindowStart { get; set; }

    /// <summary>长约窗口止（如 11:00）。</summary>
    [Column("window_end")]
    public string? WindowEnd { get; set; }

    /// <summary>长约已用预算。</summary>
    [Column("budget_used")]
    public long? BudgetUsed { get; set; }

    /// <summary>长约预算上限。</summary>
    [Column("budget_cap")]
    public long? BudgetCap { get; set; }

    /// <summary>承接方（门派 / 侠客，可空）。</summary>
    [Indexed]
    [Column("acceptor_id")]
    public string? AcceptorId { get; set; }

    /// <summary>标签数组 JSON（公开贴榜 / 非新手 / 新手专属 …）。</summary>
    [Column("tags_json")]
    public string? TagsJson { get; set; }

    /// <summary>是否新手专属。</summary>
    [Column("novice_only")]
    public bool NoviceOnly { get; set; }

    /// <summary>无人接已等待周期数。</summary>
    [Column("waiting_cycles")]
    public int WaitingCycles { get; set; }

    /// <summary>最后更新时间（Unix 毫秒）。</summary>
    [Column("updated_at_ms")]
    public long UpdatedAtMs { get; set; }
}
