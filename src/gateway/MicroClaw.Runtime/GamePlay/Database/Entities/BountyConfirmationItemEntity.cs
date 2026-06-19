using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 悬赏待确认项（执行前需主公 / 发起人补充）。如支付方式范围、回调地址、验收口径。
/// 对应 bounty-board 待确认行展开表单。
/// </summary>
[Table("bounty_confirmation_item")]
public class BountyConfirmationItemEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>悬赏 Id。</summary>
    [Indexed]
    [Column("bounty_id")]
    public string BountyId { get; set; } = string.Empty;

    /// <summary>确认项键：payment-method / callback-url / acceptance。</summary>
    [Column("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>展示名（支付方式范围 / 回调地址 / 验收口径）。</summary>
    [Column("label")]
    public string? Label { get; set; }

    /// <summary>确认人（主公 / 飞书驿）。</summary>
    [Column("confirmer")]
    public string? Confirmer { get; set; }

    /// <summary>字段类型：Checkbox / Url / Textarea / Select。</summary>
    [Column("field_type")]
    public string? FieldType { get; set; }

    /// <summary>填写值（JSON，依 field_type 解析）。</summary>
    [Column("value_json")]
    public string? ValueJson { get; set; }

    /// <summary>状态：Pending 待补充 / Confirmed 已确认 / Sent 已发信报。</summary>
    [Column("status")]
    public string Status { get; set; } = "Pending";

    /// <summary>是否阻塞悬赏推进。</summary>
    [Column("is_blocking")]
    public bool IsBlocking { get; set; }

    /// <summary>最后更新时间（Unix 毫秒）。</summary>
    [Column("updated_at_ms")]
    public long UpdatedAtMs { get; set; }
}
