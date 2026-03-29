namespace MicroClaw.RAG;

/// <summary>
/// 混合检索结果条目。
/// </summary>
public sealed record HybridSearchResult(
    VectorChunkEntity Record,
    float SemanticScore,
    float KeywordScore,
    float FusedScore);
