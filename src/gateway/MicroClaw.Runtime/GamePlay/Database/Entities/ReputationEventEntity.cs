using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 声望变动事件流（append-only）。审计与回放，结算时累加进 member_domain_reputation 快照。
/// </summary>
[Table("reputation_event")]
public class ReputationEventEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>目标类型：Member / Faction。</summary>
    [Column("target_type")]
    public string TargetType { get; set; } = "Member";

    /// <summary>目标 Id（member_id 或 faction_id）。</summary>
    [Indexed]
    [Column("target_id")]
    public string TargetId { get; set; } = string.Empty;

    /// <summary>领域（门派声望无领域维度时可空）。</summary>
    [Column("domain")]
    public string? Domain { get; set; }

    /// <summary>声望变动量（正 / 负）。</summary>
    [Column("delta")]
    public int Delta { get; set; }

    /// <summary>变动原因（完成悬赏 / 古籍研习 / 败阵）。</summary>
    [Column("reason")]
    public string? Reason { get; set; }

    /// <summary>发生周期。</summary>
    [Indexed]
    [Column("cycle_number")]
    public int CycleNumber { get; set; }

    /// <summary>发生时间（Unix 毫秒）。</summary>
    [Column("created_at_ms")]
    public long CreatedAtMs { get; set; }
}
