namespace MicroClaw.RAG;

/// <summary>
/// Reserved interface: selects a suitable Embedding Provider ID from configuration.
/// Future RAG vectorization pipeline will route embedding calls via this interface.
/// </summary>
public interface IEmbeddingProviderResolver
{
    /// <summary>Returns the ID of the preferred enabled Embedding Provider; null if none exists.</summary>
    string? GetPreferredEmbeddingProviderId();
}
