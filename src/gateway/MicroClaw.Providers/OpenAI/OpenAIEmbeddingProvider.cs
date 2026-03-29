using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;

namespace MicroClaw.Providers.OpenAI;

/// <summary>
/// OpenAI 嵌入提供者 — 使用 OpenAI Embeddings API 创建 <see cref="IEmbeddingGenerator{String, Embedding}"/>。
/// </summary>
public sealed class OpenAIEmbeddingProvider : IEmbeddingProvider
{
    public bool Supports(ProviderProtocol protocol) => protocol == ProviderProtocol.OpenAI;

    public IEmbeddingGenerator<string, Embedding<float>> Create(ProviderConfig config)
    {
        OpenAIClientOptions options = new();
        if (!string.IsNullOrWhiteSpace(config.BaseUrl))
            options.Endpoint = new Uri(config.BaseUrl);

        var credential = new ApiKeyCredential(config.ApiKey);
        var client = new OpenAIClient(credential, options);
        return client.GetEmbeddingClient(config.ModelName).AsIEmbeddingGenerator();
    }
}
