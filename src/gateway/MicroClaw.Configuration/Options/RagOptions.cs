namespace MicroClaw.Configuration;

/// <summary>
/// RAG auto-forget configuration. Mapped to YAML section "rag".
/// </summary>
public sealed class RagOptions
{
    /// <summary>Maximum storage size (MB) per session RAG DB. When exceeded, low-HitCount chunks are pruned.</summary>
    public double MaxStorageSizeMb { get; set; } = 50;

    /// <summary>Target size after pruning, as a fraction of MaxStorageSizeMb (0.0–1.0).</summary>
    public double PruneTargetPercent { get; set; } = 0.8;
}
