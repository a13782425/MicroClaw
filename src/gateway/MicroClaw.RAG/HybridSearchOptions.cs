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

    /// <summary>
    /// 是否启用记忆衰减（默认 false）。
    /// 启用后，长期未被访问的分块检索得分将降低。
    /// </summary>
    public bool EnableDecay { get; init; } = false;

    /// <summary>
    /// 衰减半衰期（天，默认 90）。
    /// 即分块自最近访问时间起过了此天数，得分乘以 0.5。
    /// </summary>
    public float DecayHalfLifeDays { get; init; } = 90f;
}
