namespace MicroClaw.RAG;

/// <summary>
/// RAG 服务接口（预留，向量存储实现待定）
/// </summary>
public interface IRagService
{
    /// <summary>查询 RAG 知识库</summary>
    Task<string> QueryAsync(string query, RagScope scope, string? sessionId, CancellationToken ct = default);

    /// <summary>向 RAG 知识库注入数据（自动生成 SourceId）</summary>
    Task IngestAsync(string source, RagScope scope, string? sessionId, CancellationToken ct = default);

    /// <summary>
    /// 向 RAG 知识库注入数据，使用指定的 <paramref name="sourceId"/>（用于增量索引去重）。
    /// 若 DB 中已存在相同 SourceId 的分块，则跳过以保证幂等性。
    /// </summary>
    Task IngestAsync(string source, string sourceId, RagScope scope, string? sessionId, CancellationToken ct = default);

    /// <summary>
    /// 获取目标 RAG 库中所有已索引的 SourceId 集合，供增量索引时判断哪些内容已存在。
    /// </summary>
    Task<IReadOnlySet<string>> GetIndexedSourceIdsAsync(RagScope scope, string? sessionId, CancellationToken ct = default);

    /// <summary>
    /// 将文档上传至全局知识库。
    /// <list type="bullet">
    ///   <item>SourceId 固定为 <c>doc:{fileName}</c>。</item>
    ///   <item>若同名文档已存在，则先删除旧分块再重新索引（增量重索引）。</item>
    ///   <item>MetadataJson 记录 <c>filename</c>，供 <see cref="ListDocumentsAsync"/> 聚合使用。</item>
    /// </list>
    /// </summary>
    /// <returns>该文档的 SourceId（<c>doc:{fileName}</c>）。</returns>
    Task<string> IngestDocumentAsync(string source, string fileName, RagScope scope, string? sessionId, CancellationToken ct = default);

    /// <summary>
    /// 删除指定 SourceId 的所有向量分块。
    /// </summary>
    Task DeleteBySourceIdAsync(string sourceId, RagScope scope, string? sessionId, CancellationToken ct = default);

    /// <summary>
    /// 列出知识库中所有以 <c>doc:</c> 前缀标识的文档，按 SourceId 聚合。
    /// </summary>
    Task<IReadOnlyList<RagDocumentInfo>> ListDocumentsAsync(RagScope scope, string? sessionId, CancellationToken ct = default);

    /// <summary>
    /// 获取 RAG 检索聚合统计数据。
    /// </summary>
    /// <param name="scope">过滤作用域（null 表示全部）。</param>
    /// <param name="ct">取消令牌。</param>
    Task<RagQueryStats> GetQueryStatsAsync(RagScope? scope, CancellationToken ct = default);

    /// <summary>
    /// 结构化检索：返回格式化文本 + 分块引用列表（含命中次数），
    /// 供 <see cref="RagContextProvider"/> 注入 System Prompt 和后续 AI 审计使用。
    /// </summary>
    Task<RagQueryResult> QueryWithMetadataAsync(string query, RagScope scope, string? sessionId, CancellationToken ct = default);

    /// <summary>
    /// 列出指定作用域下的所有分块（不限于 doc: 前缀），供前端管理界面使用。
    /// </summary>
    Task<IReadOnlyList<RagChunkInfo>> ListChunksAsync(RagScope scope, string? sessionId, CancellationToken ct = default);

    /// <summary>
    /// 删除指定 ID 的单个分块。
    /// </summary>
    Task DeleteChunkAsync(string chunkId, RagScope scope, string? sessionId, CancellationToken ct = default);

    /// <summary>
    /// 手动设置指定分块的命中次数（用于用户修正 AI 审计结果）。
    /// </summary>
    Task UpdateChunkHitCountAsync(string chunkId, int hitCount, RagScope scope, string? sessionId, CancellationToken ct = default);

    /// <summary>
    /// 批量增加指定分块的命中次数（+1），由 AI 审计服务在确认实际使用后调用。
    /// </summary>
    Task IncrementHitCountAsync(IReadOnlyList<string> chunkIds, RagScope scope, string? sessionId, CancellationToken ct = default);
}
