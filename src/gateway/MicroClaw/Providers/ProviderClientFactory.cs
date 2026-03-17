using Anthropic.SDK;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using MicroClaw.Provider.Abstractions;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Responses;
using System.ClientModel;

namespace MicroClaw.Providers;

public sealed class ProviderClientFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public ProviderClientFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IChatClient Create(ProviderConfig config) =>
        config.Protocol switch
        {
            ProviderProtocol.OpenAI => CreateOpenAI(config),
            ProviderProtocol.OpenAIResponses => CreateOpenAIResponses(config),
            ProviderProtocol.Anthropic => CreateAnthropic(config),
            _ => throw new NotSupportedException($"Protocol '{config.Protocol}' is not supported.")
        };

    private IChatClient CreateOpenAI(ProviderConfig config)
    {
        OpenAIClientOptions options = new();
        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
            options.Endpoint = new Uri(config.BaseUrl);

        ChatClient client = new(config.ModelName, new ApiKeyCredential(config.ApiKey), options);
        return new ChatClientBuilder(client.AsIChatClient())
            .UseLogging(_loggerFactory)
            .Build();
    }

    private IChatClient CreateOpenAIResponses(ProviderConfig config)
    {
        OpenAIClientOptions options = new();
        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
            options.Endpoint = new Uri(config.BaseUrl);

        ResponsesClient client = new(new ApiKeyCredential(config.ApiKey), options);
        return new ChatClientBuilder(client.AsIChatClient(config.ModelName))
            .UseLogging(_loggerFactory)
            .Build();
    }

    private IChatClient CreateAnthropic(ProviderConfig config)
    {
        AnthropicClient client = new(new APIAuthentication(config.ApiKey));
        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
            client.ApiUrlFormat = config.BaseUrl.TrimEnd('/') + "/{0}/{1}";

        return new ChatClientBuilder(client.Messages)
            .UseLogging(_loggerFactory)
            .Build();
    }
}
