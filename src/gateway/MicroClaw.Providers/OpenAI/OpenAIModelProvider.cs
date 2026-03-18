using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace MicroClaw.Providers.OpenAI;

/// <summary>
/// IModelProvider implementation for the OpenAI Chat Completions API.
/// Supports official OpenAI and any OpenAI-compatible endpoint (DeepSeek, Qwen, LiteLLM, etc.).
/// Set config.Capabilities.SupportsResponsesApi = true to signal Responses API support
/// (actual OpenAI Responses API routing is planned for a future iteration; currently always uses ChatClient).
/// </summary>
public sealed class OpenAIModelProvider : IModelProvider
{
    private readonly ILoggerFactory _loggerFactory;

    public OpenAIModelProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public bool Supports(ProviderProtocol protocol) => protocol == ProviderProtocol.OpenAI;

    public IChatClient Create(ProviderConfig config)
    {
        OpenAIClientOptions options = new();
        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
            options.Endpoint = new Uri(config.BaseUrl);

        ChatClient client = new(config.ModelName, new ApiKeyCredential(config.ApiKey), options);
        return new ChatClientBuilder(client.AsIChatClient())
            .UseLogging(_loggerFactory)
            .Build();
    }
}
