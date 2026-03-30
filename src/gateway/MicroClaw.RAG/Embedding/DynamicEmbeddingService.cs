using Microsoft.Extensions.Logging;

namespace MicroClaw.RAG;

/// <summary>
/// 动态 <see cref="IEmbeddingService"/> — 每次调用时通过 <see cref="IEmbeddingProviderAccessor"/>
/// 获取当前启用的 Embedding Provider，无需重启即可热切换 Provider。
/// </summary>
public sealed class DynamicEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingProviderAccessor _accessor;
    private readonly ILogger<DynamicEmbeddingService> _logger;

    public DynamicEmbeddingService(
        IEmbeddingProviderAccessor accessor,
        ILogger<DynamicEmbeddingService> logger)
    {
        _accessor = accessor ?? throw new ArgumentNullException(nameof(accessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ReadOnlyMemory<float>> GenerateAsync(string text, CancellationToken ct = default)
    {
        var service = _accessor.GetCurrentService();
        if (service is null)
        {
            _logger.LogWarning("未找到可用的 Embedding Provider，返回空向量");
            return ReadOnlyMemory<float>.Empty;
        }
        return await service.GenerateAsync(text, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReadOnlyMemory<float>>> GenerateBatchAsync(
        IEnumerable<string> texts, CancellationToken ct = default)
    {
        var service = _accessor.GetCurrentService();
        if (service is null)
        {
            _logger.LogWarning("未找到可用的 Embedding Provider，返回空向量列表");
            return texts.Select(_ => ReadOnlyMemory<float>.Empty).ToList();
        }
        return await service.GenerateBatchAsync(texts, ct).ConfigureAwait(false);
    }
}
