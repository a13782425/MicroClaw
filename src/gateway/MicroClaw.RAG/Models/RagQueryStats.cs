namespace MicroClaw.RAG;

/// <summary>
/// RAG 检索聚合统计数据。
/// </summary>
public sealed record RagQueryStats(
    /// <summary>统计口径（"All" / "Global" / "Session"）。</summary>
    string Scope,
    /// <summary>总查询次数。</summary>
    long TotalQueries,
    /// <summary>命中查询次数（召回数 > 0）。</summary>
    long HitQueries,
    /// <summary>命中率（0~1）；无数据时为 0。</summary>
    double HitRate,
    /// <summary>平均检索延迟（毫秒）；无数据时为 0。</summary>
    double AvgElapsedMs,
    /// <summary>平均召回数量；无数据时为 0。</summary>
    double AvgRecallCount,
    /// <summary>最近 24 小时内的查询次数。</summary>
    long Last24hQueries);
