using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

#pragma warning disable OPENAI001 // ResponsesClient is marked experimental

namespace MicroClaw.Providers.OpenAI;

/// <summary>
/// IModelProvider implementation for OpenAI models.
/// Supports both Chat Completions API (default) and Responses API paths.
/// When <see cref="ProviderCapabilities.SupportsResponsesApi"/> is true,
/// uses <c>ResponsesClient.AsIChatClient()</c> which enables built-in tools,
/// conversation state management, and other Responses API features.
/// Both paths produce an <see cref="IChatClient"/>, so downstream consumers are unaffected.
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

        var credential = new ApiKeyCredential(config.ApiKey);

        // 有自定义 BaseUrl 时降级为 Chat Completions（ResponsesAPI 需官方端点）
        bool useResponsesApi = config.Capabilities.SupportsResponsesApi
            && string.IsNullOrWhiteSpace(config.BaseUrl);

        IChatClient inner;
        if (useResponsesApi)
        {
            var openAiClient = new OpenAIClient(credential, options);
            inner = openAiClient.GetResponsesClient().AsIChatClient(config.ModelName);
        }
        else
        {
            inner = new ChatClient(config.ModelName, credential, options).AsIChatClient();
        }

        return new ChatClientBuilder(inner)
            .UseLogging(_loggerFactory)
            .Build();
    }
}
