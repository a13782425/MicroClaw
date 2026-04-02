using Microsoft.Extensions.Configuration;

namespace MicroClaw.Configuration;

/// <summary>
/// RAG auto-forget configuration. Mapped to YAML section "rag".
/// </summary>
public sealed class RagOptions
{
    /// <summary>Maximum storage size (MB) per session RAG DB. When exceeded, low-HitCount chunks are pruned.</summary>
    [ConfigurationKeyName("max_storage_size_mb")]
    public double MaxStorageSizeMb { get; set; } = 50;

    /// <summary>Target size after pruning, as a fraction of MaxStorageSizeMb (0.0–1.0).</summary>
    [ConfigurationKeyName("prune_target_percent")]
    public double PruneTargetPercent { get; set; } = 0.8;
}
