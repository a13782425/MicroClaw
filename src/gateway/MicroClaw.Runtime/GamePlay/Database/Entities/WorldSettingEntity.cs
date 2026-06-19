using MicroClaw.Database;
using SQLite;

namespace MicroClaw.Runtime.GamePlay;

/// <summary>
/// 游戏态设置：税率、免税线、晨钟节律。每个江湖单行，Id 等于 WorldId。economy 页读写。
/// </summary>
[Table("world_setting")]
public class WorldSettingEntity : IDatabaseEntity
{
    /// <summary>主键，等于 world_id。</summary>
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>门派所得税率（千分比，1200 = 12.0%）。</summary>
    [Column("sect_tax_rate")]
    public int SectTaxRate { get; set; }

    /// <summary>门派免税线（两）。</summary>
    [Column("sect_exempt_line")]
    public long SectExemptLine { get; set; }

    /// <summary>弟子所得税率（千分比）。</summary>
    [Column("disciple_tax_rate")]
    public int DiscipleTaxRate { get; set; }

    /// <summary>弟子免税线（两）。</summary>
    [Column("disciple_exempt_line")]
    public long DiscipleExemptLine { get; set; }

    /// <summary>晨钟暮鼓节律（帧）。</summary>
    [Column("tick_frame")]
    public int TickFrame { get; set; } = 10;

    /// <summary>最后更新时间（Unix 毫秒）。</summary>
    [Column("updated_at_ms")]
    public long UpdatedAtMs { get; set; }
}
