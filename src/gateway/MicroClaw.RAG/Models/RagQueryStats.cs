namespace MicroClaw.RAG;

/// <summary>
/// RAG 检索聚合统计数据。
/// <para>
/// 同时携带原始累计值（便于跨 RAG 求和合并）与派生平均值（便于前端直接展示）。
/// 使用 <see cref="Derive"/> 工厂从累计值构造时自动计算派生字段并处理除零。
/// </para>
/// </summary>
public sealed record RagQueryStats(
    /// <summary>统计口径（通常为 RAG 数据库文件名，例如 "globalrag" 或 "rag"）。</summary>
    string Scope,
    /// <summary>累计检索次数。</summary>
    long TotalQueries,
    /// <summary>命中次数（召回结果 &gt; 0）。未命中 = TotalQueries - HitQueries。</summary>
    long HitQueries,
    /// <summary>累计检索耗时（毫秒，原始值，供合并使用）。</summary>
    long TotalElapsedMs,
    /// <summary>累计召回 chunk 数（原始值，供合并使用）。</summary>
    long TotalRecallCount,
    /// <summary>命中率（0~1）；无数据时为 0。</summary>
    double HitRate,
    /// <summary>平均检索延迟（毫秒）；无数据时为 0。</summary>
    double AvgElapsedMs,
    /// <summary>平均召回数量；无数据时为 0。</summary>
    double AvgRecallCount)
{
    /// <summary>
    /// 从累计值构造 <see cref="RagQueryStats"/>，自动计算派生字段（命中率、平均延迟、平均召回数），并处理除零。
    /// </summary>
    public static RagQueryStats Derive(
        string scope,
        long totalQueries,
        long hitQueries,
        long totalElapsedMs,
        long totalRecallCount)
    {
        if (totalQueries <= 0)
            return new RagQueryStats(scope, 0, 0, 0, 0, 0, 0, 0);

        double hitRate = (double)hitQueries / totalQueries;
        double avgElapsed = Math.Round((double)totalElapsedMs / totalQueries, 1);
        double avgRecall = Math.Round((double)totalRecallCount / totalQueries, 2);

        return new RagQueryStats(
            scope,
            totalQueries,
            hitQueries,
            totalElapsedMs,
            totalRecallCount,
            hitRate,
            avgElapsed,
            avgRecall);
    }
}
