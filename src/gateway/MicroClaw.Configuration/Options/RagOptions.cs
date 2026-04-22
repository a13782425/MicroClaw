
namespace MicroClaw.Configuration;

/// <summary>
/// RAG auto-forget configuration. Mapped to YAML section "rag".
/// </summary>
[MicroClawYamlConfig("rag", FileName = "rag.yaml", IsWritable = true)]
public sealed class RagOptions : IMicroClawConfigTemplate
{
    /// <summary>Maximum storage size (MB) per session RAG DB. When exceeded, low-HitCount chunks are pruned.</summary>
    [YamlMember(Alias = "max_storage_size_mb")]
    public double MaxStorageSizeMb { get; set; } = 50;

    /// <summary>Target size after pruning, as a fraction of MaxStorageSizeMb (0.0–1.0).</summary>
    [YamlMember(Alias = "prune_target_percent")]
    public double PruneTargetPercent { get; set; } = 0.8;

    public IMicroClawConfigOptions CreateDefaultTemplate() => new RagOptions();
}
