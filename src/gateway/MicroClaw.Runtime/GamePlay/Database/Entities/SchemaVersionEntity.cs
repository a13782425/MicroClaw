using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 库结构版本号（迁移用）。每个江湖库单行，Id 固定为 "single"。
/// </summary>
[Table("schema_version")]
public class SchemaVersionEntity : IDatabaseEntity
{
    /// <summary>主键，固定 "single"。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = "single";

    /// <summary>当前 schema 版本号。</summary>
    [Column("version")]
    public int Version { get; set; }

    /// <summary>该版本应用时间（Unix 毫秒）。</summary>
    [Column("applied_at_ms")]
    public long AppliedAtMs { get; set; }
}
