using Anthropic.SDK;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Providers.Claude;

/// <summary>
/// IModelProvider implementation for the Anthropic Claude API.
/// Extend Create() via ChatClientBuilder middleware to add Memory or RAG.
/// </summary>
public sealed class AnthropicModelProvider : IModelProvider
{
    private readonly ILoggerFactory _loggerFactory;

    public AnthropicModelProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public bool Supports(ProviderProtocol protocol) => protocol == ProviderProtocol.Anthropic;

    public IChatClient Create(ProviderConfig config)
    {
        AnthropicClient client = new(new APIAuthentication(config.ApiKey));
        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
            client.ApiUrlFormat = config.BaseUrl.TrimEnd('/') + "/{0}/{1}";

        return new ChatClientBuilder(client.Messages)
            .UseLogging(_loggerFactory)
            .Build();
    }
}
