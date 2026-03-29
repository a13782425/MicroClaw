using Microsoft.Extensions.AI;

namespace MicroClaw.RAG;

/// <summary>
/// <see cref="IEmbeddingService"/> 实现 — 委托给 <see cref="IEmbeddingGenerator{String, Embedding}"/>。
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;

    public EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> generator)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
    }

    public async Task<ReadOnlyMemory<float>> GenerateAsync(string text, CancellationToken ct = default)
    {
        var results = await _generator.GenerateAsync([text], cancellationToken: ct);
        return results[0].Vector;
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        var results = await _generator.GenerateAsync(texts.ToList(), cancellationToken: ct);
        return results.Select(e => e.Vector).ToList();
    }
}
