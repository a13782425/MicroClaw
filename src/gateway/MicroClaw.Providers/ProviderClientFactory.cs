using Microsoft.Extensions.AI;

namespace MicroClaw.Providers;

/// <summary>
/// Dispatches IChatClient creation to the registered <see cref="IModelProvider"/> that
/// supports the requested <see cref="ProviderProtocol"/>.
/// Each provider implementation (OpenAI, OpenAICompatible, Claude …) lives in its own
/// project and can independently add Memory / RAG middleware to the ChatClientBuilder.
/// </summary>
public sealed class ProviderClientFactory
{
    private readonly IEnumerable<IModelProvider> _providers;

    public ProviderClientFactory(IEnumerable<IModelProvider> providers)
    {
        _providers = providers;
    }

    public IChatClient Create(ProviderConfig config)
    {
        IModelProvider? provider = _providers.FirstOrDefault(p => p.Supports(config.Protocol));
        if (provider is null)
            throw new NotSupportedException(
                $"Protocol '{config.Protocol}' is not supported. " +
                "Ensure the corresponding IModelProvider is registered in DI.");

        return provider.Create(config);
    }
}
