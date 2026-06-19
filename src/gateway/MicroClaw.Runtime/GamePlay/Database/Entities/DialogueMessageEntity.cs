using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 盟主传音消息。主公 ↔ 盟主双向；支持文本 / 裁决方案 / 各类媒体。对应 dialogue 页。
/// </summary>
[Table("dialogue_message")]
public class DialogueMessageEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>发送方：Lord 主公 / Alliance 盟主。</summary>
    [Column("sender")]
    public string Sender { get; set; } = "Alliance";

    /// <summary>消息类型：Text / Decision / Video / Audio / Image / File。</summary>
    [Column("kind")]
    public string Kind { get; set; } = "Text";

    /// <summary>文本内容（可空，纯媒体消息时）。</summary>
    [Column("text")]
    public string? Text { get; set; }

    /// <summary>裁决方案 Id（kind = Decision 时指向 decision_proposal，可空）。</summary>
    [Column("proposal_id")]
    public string? ProposalId { get; set; }

    /// <summary>游戏内周期（甲子年霜月初三等由此派生格式化）。</summary>
    [Column("cycle_number")]
    public int CycleNumber { get; set; }

    /// <summary>发送时间（Unix 毫秒，用于排序）。</summary>
    [Indexed]
    [Column("sent_at_ms")]
    public long SentAtMs { get; set; }
}
