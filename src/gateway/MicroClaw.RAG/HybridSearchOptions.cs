namespace MicroClaw.RAG;

/// <summary>
/// 混合检索配置。
/// </summary>
public sealed record HybridSearchOptions
{
    /// <summary>语义检索权重（默认 0.7）。</summary>
    public float SemanticWeight { get; init; } = 0.7f;

    /// <summary>关键词检索权重（默认 0.3）。</summary>
    public float KeywordWeight { get; init; } = 0.3f;

    /// <summary>返回结果数上限（默认 10）。</summary>
    public int TopK { get; init; } = 10;

    /// <summary>语义检索候选池大小倍数（TopK × 此值 = 实际语义检索量）。</summary>
    public int SemanticCandidateMultiplier { get; init; } = 3;
}
