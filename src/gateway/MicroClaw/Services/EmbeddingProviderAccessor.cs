using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers;
using MicroClaw.RAG;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Services;

/// <summary>
/// 动态 Embedding Provider 访问器 — 实现 <see cref="IEmbeddingProviderAccessor"/>。
/// 每次调用 <see cref="GetCurrentService"/> 时实时读取已启用的 Embedding 配置，
/// 支持无重启热切换；当 Provider ID 不变时复用已缓存的生成器实例。
/// </summary>
public sealed class EmbeddingProviderAccessor : IEmbeddingProviderAccessor
{
    private readonly ProviderService _configStore;
    private readonly ProviderEmbeddingFactory _factory;
    private readonly IUsageTracker _usageTracker;
    private readonly ILogger<EmbeddingProviderAccessor> _logger;

    private readonly Lock _lock = new();
    private string? _cachedCacheKey;
    private IEmbeddingService? _cachedService;

    /// <summary>
    /// 生成缓存键，包含所有影响 EmbeddingGenerator 实例的字段。
    /// 使用 ApiKey 的 HashCode 避免明文存储，同进程内字符串 HashCode 稳定。
    /// </summary>
    private static string ComputeCacheKey(ProviderConfig config) =>
        $"{config.Id}|{config.Protocol}|{config.ModelName}|{config.BaseUrl}|{config.ApiKey.GetHashCode():X8}";

    public EmbeddingProviderAccessor(
        ProviderService configStore,
        ProviderEmbeddingFactory factory,
        IUsageTracker usageTracker,
        ILogger<EmbeddingProviderAccessor> logger)
    {
        _configStore = configStore ?? throw new ArgumentNullException(nameof(configStore));
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _usageTracker = usageTracker ?? throw new ArgumentNullException(nameof(usageTracker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public IEmbeddingService? GetCurrentService()
    {
        var providers = _configStore.GetEmbeddingProviders();
        var config = providers.FirstOrDefault();

        if (config is null)
            return null;

        lock (_lock)
        {
            var cacheKey = ComputeCacheKey(config);

            if (_cachedCacheKey == cacheKey && _cachedService is not null)
                return _cachedService;

            if (_cachedCacheKey is not null && _cachedCacheKey != cacheKey)
                _logger.LogInformation("Embedding Provider 配置已变更，重建客户端: {Name} ({Id})", config.DisplayName, config.Id);

            var capturedId = config.Id;
            var capturedName = config.DisplayName;
            var inputPrice = config.Capabilities.InputPricePerMToken ?? 0m;

            _cachedService = new EmbeddingService(
                _factory.Create(config),
                tokens =>
                {
                    var cost = inputPrice * tokens / 1_000_000m;
                    _ = _usageTracker.TrackAsync(
                        sessionId: null,
                        providerId: capturedId,
                        providerName: capturedName,
                        source: "rag-embed",
                        inputTokens: tokens,
                        outputTokens: 0,
                        inputCostUsd: cost);
                });

            _cachedCacheKey = cacheKey;
            return _cachedService;
        }
    }
}
