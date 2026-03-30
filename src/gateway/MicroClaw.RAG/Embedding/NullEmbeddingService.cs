namespace MicroClaw.RAG;

/// <summary>
/// 空实现 <see cref="IEmbeddingService"/> — 当没有配置嵌入提供者时作为回退。
/// 始终返回空向量，语义检索将降级为纯关键词检索。
/// </summary>
public sealed class NullEmbeddingService : IEmbeddingService
{
    public Task<ReadOnlyMemory<float>> GenerateAsync(string text, CancellationToken ct = default)
        => Task.FromResult(ReadOnlyMemory<float>.Empty);

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        IReadOnlyList<ReadOnlyMemory<float>> result =
            texts.Select(_ => ReadOnlyMemory<float>.Empty).ToList();
        return Task.FromResult(result);
    }
}
