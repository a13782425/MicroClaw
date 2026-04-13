using Microsoft.Extensions.AI;

namespace MicroClaw.Providers;

/// <summary>
/// 嵌入生成器工厂 — 根据 <see cref="ProviderConfig"/> 路由到正确的 <see cref="IEmbeddingProvider"/>。
/// 用法与 <see cref="ProviderService"/> 平行。
/// </summary>
public sealed class ProviderEmbeddingFactory
{
    private readonly IEnumerable<IEmbeddingProvider> _providers;

    public ProviderEmbeddingFactory(IEnumerable<IEmbeddingProvider> providers)
    {
        _providers = providers;
    }

    public IEmbeddingGenerator<string, Embedding<float>> Create(ProviderConfig config)
    {
        IEmbeddingProvider? provider = _providers.FirstOrDefault(p => p.Supports(config.Protocol));
        if (provider is null)
            throw new NotSupportedException(
                $"Protocol '{config.Protocol}' does not support embedding generation. " +
                "Ensure the corresponding IEmbeddingProvider is registered in DI.");

        return provider.Create(config);
    }
}
