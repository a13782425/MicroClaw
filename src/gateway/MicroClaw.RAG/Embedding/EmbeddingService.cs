using Microsoft.Extensions.AI;

namespace MicroClaw.RAG;

/// <summary>
/// <see cref="IEmbeddingService"/> 实现 — 委托给 <see cref="IEmbeddingGenerator{String, Embedding}"/>。
/// 可选注入 <paramref name="onTokensUsed"/> 回调，每次调用后上报 Embedding Token 消耗以统计费用。
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingGenerator<string, Embedding<float>> _generator;
    private readonly Action<long>? _onTokensUsed;

    public EmbeddingService(
        IEmbeddingGenerator<string, Embedding<float>> generator,
        Action<long>? onTokensUsed = null)
    {
        _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _onTokensUsed = onTokensUsed;
    }

    public async Task<ReadOnlyMemory<float>> GenerateAsync(string text, CancellationToken ct = default)
    {
        var results = await _generator.GenerateAsync([text], cancellationToken: ct);
        ReportUsage(results.Usage);
        return results[0].Vector;
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        var results = await _generator.GenerateAsync(texts.ToList(), cancellationToken: ct);
        ReportUsage(results.Usage);
        return results.Select(e => e.Vector).ToList();
    }

    private void ReportUsage(UsageDetails? usage)
    {
        if (_onTokensUsed is null) return;
        var tokens = usage?.InputTokenCount ?? 0L;
        if (tokens > 0) _onTokensUsed(tokens);
    }
}
