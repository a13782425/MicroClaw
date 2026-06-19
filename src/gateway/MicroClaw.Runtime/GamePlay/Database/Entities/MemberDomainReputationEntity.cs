using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 六维声望快照（雷达用）。每个成员每个领域一行；由 reputation_event 累加驱动。
/// (member_id, domain) 唯一。
/// </summary>
[Table("member_domain_reputation")]
public class MemberDomainReputationEntity : IDatabaseEntity
{
    /// <summary>主键，hash(world_id + member_id + domain)。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>成员 Id。</summary>
    [Indexed("ix_mdr_member_domain", 1, Unique = true)]
    [Column("member_id")]
    public string MemberId { get; set; } = string.Empty;

    /// <summary>领域：Web前端 / 后端服务 / 数据库 / 架构设计 / 安全攻防 / 运维部署（可扩展）。</summary>
    [Indexed("ix_mdr_member_domain", 2, Unique = true)]
    [Column("domain")]
    public string Domain { get; set; } = string.Empty;

    /// <summary>声望值（0~100）。</summary>
    [Column("value")]
    public int Value { get; set; }

    /// <summary>最后更新时间（Unix 毫秒）。</summary>
    [Column("updated_at_ms")]
    public long UpdatedAtMs { get; set; }
}
