namespace MicroClaw.RAG;

/// <summary>
/// RAG 检索累计统计（每个 RAG 数据库单行记录）。
/// <para>
/// 表：<c>search_stats</c>，固定单行（<see cref="Id"/> = 1），所有字段均为累加型。
/// 未命中次数可由 <c>TotalQueries - HitQueries</c> 派生，不单独存储。
/// </para>
/// </summary>
public sealed class RagSearchStatEntity
{
    /// <summary>固定主键，始终为 1（单行模型）。</summary>
    public int Id { get; set; } = 1;

    /// <summary>累计检索次数（含命中与未命中）。</summary>
    public long TotalQueries { get; set; }

    /// <summary>命中次数（召回结果 &gt; 0）。未命中 = <see cref="TotalQueries"/> - <see cref="HitQueries"/>。</summary>
    public long HitQueries { get; set; }

    /// <summary>累计检索耗时（毫秒），用于计算平均延迟。</summary>
    public long TotalElapsedMs { get; set; }

    /// <summary>累计召回 chunk 数，用于计算平均召回数。</summary>
    public long TotalRecallCount { get; set; }

    /// <summary>最后一次写入时间（Unix 毫秒）。</summary>
    public long LastUpdatedAtMs { get; set; }
}
