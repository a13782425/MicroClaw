using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 消息附件 / 媒体 / 裁决卡。一条消息可挂多个附件。对应 dialogue 页的附件展示。
/// </summary>
[Table("dialogue_attachment")]
public class DialogueAttachmentEntity : IDatabaseEntity
{
    /// <summary>主键。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>所属江湖（隔离用）。</summary>
    [Column("world_id")]
    public string WorldId { get; set; } = string.Empty;

    /// <summary>所属消息 Id。</summary>
    [Indexed]
    [Column("message_id")]
    public string MessageId { get; set; } = string.Empty;

    /// <summary>附件类型：Image / Video / Audio / File / DecisionCard。</summary>
    [Column("type")]
    public string Type { get; set; } = "File";

    /// <summary>文件名（藏经阁出账明细.csv）。</summary>
    [Column("name")]
    public string? Name { get; set; }

    /// <summary>描述。</summary>
    [Column("desc")]
    public string? Desc { get; set; }

    /// <summary>文件大小（字节，可空）。</summary>
    [Column("size_bytes")]
    public long? SizeBytes { get; set; }

    /// <summary>音视频时长（毫秒，可空）。</summary>
    [Column("duration_ms")]
    public long? DurationMs { get; set; }

    /// <summary>优先级：高 / 中 / 低（可空）。</summary>
    [Column("priority")]
    public string? Priority { get; set; }

    /// <summary>载荷 JSON（DecisionCard 条目 / media 路径等，可空）。</summary>
    [Column("payload_json")]
    public string? PayloadJson { get; set; }
}
