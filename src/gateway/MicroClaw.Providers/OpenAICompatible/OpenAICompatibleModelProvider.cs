using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace MicroClaw.Providers.OpenAICompatible;

/// <summary>
/// IModelProvider implementation for OpenAI-compatible APIs.
/// Supports any endpoint that speaks the OpenAI Chat Completions protocol
/// (e.g. DeepSeek, Qwen, local LLMs via LiteLLM).
/// </summary>
public sealed class OpenAICompatibleModelProvider : IModelProvider
{
    private readonly ILoggerFactory _loggerFactory;

    public OpenAICompatibleModelProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public bool Supports(ProviderProtocol protocol) => protocol == ProviderProtocol.OpenAIResponses;

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
