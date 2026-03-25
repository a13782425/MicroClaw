using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Providers.Claude;

/// <summary>
/// IModelProvider implementation for the Anthropic Claude API.
/// Uses the official Anthropic C# SDK with IChatClient integration.
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
        IAnthropicClient client = new AnthropicClient { ApiKey = config.ApiKey };

        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
            client = client.WithOptions(o => o with { BaseUrl = config.BaseUrl });

        return new ChatClientBuilder(client.AsIChatClient(config.ModelName))
            .UseLogging(_loggerFactory)
            .Build();
    }
}
