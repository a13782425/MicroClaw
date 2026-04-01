namespace MicroClaw.RAG;

/// <summary>
/// RAG 检索结构化结果，包含格式化文本和被检索命中的分块引用列表。
/// 用于 <see cref="IRagService.QueryWithMetadataAsync"/>。
/// </summary>
/// <param name="Content">格式化后的检索文本（含命中次数标注），可直接注入 System Prompt。</param>
/// <param name="RetrievedChunks">检索命中的分块引用列表，供审计服务判定实际使用情况。</param>
public sealed record RagQueryResult(
    string Content,
    IReadOnlyList<RagChunkRef> RetrievedChunks);

/// <summary>
/// 单个被检索命中的 RAG 分块引用。
/// </summary>
/// <param name="Id">chunk 唯一 ID。</param>
/// <param name="Content">chunk 文本内容。</param>
/// <param name="HitCount">当前累计命中次数。</param>
/// <param name="SourceId">来源标识。</param>
/// <param name="Scope">chunk 所属作用域（Global / Session）。</param>
/// <param name="SessionId">Session 作用域时的 sessionId；Global 时为 null。</param>
public sealed record RagChunkRef(
    string Id,
    string Content,
    int HitCount,
    string SourceId,
    RagScope Scope,
    string? SessionId);
