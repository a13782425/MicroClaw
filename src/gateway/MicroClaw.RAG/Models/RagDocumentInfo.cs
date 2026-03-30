namespace MicroClaw.RAG;

/// <summary>
/// 全局知识库中已索引文档的聚合信息（按 <c>doc:{fileName}</c> SourceId 聚合）。
/// </summary>
/// <param name="SourceId">SourceId，格式为 <c>doc:{fileName}</c>。</param>
/// <param name="FileName">原始文件名（含扩展名）。</param>
/// <param name="ChunkCount">该文档产生的分块数量。</param>
/// <param name="IndexedAtMs">首次索引时间（Unix 毫秒）。</param>
public record RagDocumentInfo(
    string SourceId,
    string FileName,
    int ChunkCount,
    long IndexedAtMs);
