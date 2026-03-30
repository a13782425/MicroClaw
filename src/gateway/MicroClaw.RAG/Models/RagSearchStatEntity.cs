namespace MicroClaw.RAG;

/// <summary>
/// RAG 检索统计记录实体（EF Core）。
/// 每次 <see cref="IRagService.QueryAsync"/> 调用产生一条记录。
/// </summary>
public sealed class RagSearchStatEntity
{
    /// <summary>主键（GUID）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>检索作用域（"Global" 或 "Session"）。</summary>
    public string Scope { get; set; } = string.Empty;

    /// <summary>检索耗时（毫秒）。</summary>
    public long ElapsedMs { get; set; }

    /// <summary>本次检索召回的结果数量（0 表示未命中）。</summary>
    public int RecallCount { get; set; }

    /// <summary>记录时间戳（Unix 毫秒）。</summary>
    public long RecordedAtMs { get; set; }
}
