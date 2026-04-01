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
        AnthropicClient client = new()
        {
            ApiKey = config.ApiKey,
            BaseUrl = string.IsNullOrWhiteSpace(config.BaseUrl)
                ? "https://api.anthropic.com"
                : config.BaseUrl.TrimEnd('/'),
        };

        return client.AsIChatClient(config.ModelName)
            .AsBuilder()
            .UseLogging(_loggerFactory)
            .Build();
    }
}
