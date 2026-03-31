using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;

namespace MicroClaw.Providers.OpenAI;

/// <summary>
/// OpenAI 嵌入提供者 — 使用 OpenAI Embeddings API 创建 <see cref="IEmbeddingGenerator{String, Embedding}"/>。
/// </summary>
public sealed class OpenAIEmbeddingProvider : IEmbeddingProvider
{
    private readonly ILoggerFactory _loggerFactory;

    public OpenAIEmbeddingProvider(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public bool Supports(ProviderProtocol protocol) => protocol == ProviderProtocol.OpenAI;

    public IEmbeddingGenerator<string, Embedding<float>> Create(ProviderConfig config)
    {
        var logger = _loggerFactory.CreateLogger<OpenAIEmbeddingProvider>();
        var endpoint = string.IsNullOrWhiteSpace(config.BaseUrl) ? "(OpenAI 默认)" : config.BaseUrl;
        logger.LogDebug("创建嵌入客户端 — Endpoint: {Endpoint}, Model: {Model}", endpoint, config.ModelName);

        OpenAIClientOptions options = new();
        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
            options.Endpoint = new Uri(config.BaseUrl);

        var credential = new ApiKeyCredential(config.ApiKey);
        var client = new OpenAIClient(credential, options);

        return new EmbeddingGeneratorBuilder<string, Embedding<float>>(
                client.GetEmbeddingClient(config.ModelName).AsIEmbeddingGenerator())
            .UseLogging(_loggerFactory)
            .Build();
    }
}
