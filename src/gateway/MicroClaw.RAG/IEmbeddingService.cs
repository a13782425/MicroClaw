namespace MicroClaw.RAG;

/// <summary>
/// 嵌入服务接口 — 将文本转换为向量表示。
/// </summary>
public interface IEmbeddingService
{
    /// <summary>为单条文本生成嵌入向量。</summary>
    Task<ReadOnlyMemory<float>> GenerateAsync(string text, CancellationToken ct = default);

    /// <summary>为多条文本批量生成嵌入向量。</summary>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchAsync(
        IEnumerable<string> texts, CancellationToken ct = default);
}
