using System.ClientModel;
using MicroClaw.Abstractions;
using MicroClaw.Configuration.Options;
using MicroClaw.Core.Logging;
using Microsoft.Extensions.AI;
using OpenAI;

namespace MicroClaw.Providers.OpenAI;

/// <summary>
/// OpenAI 协议的 Embedding Provider。使用 OpenAI Embeddings API 生成
/// <see cref="Embedding{Single}"/>；批量调用与 usage 追踪由
/// <see cref="EmbeddingMicroProvider"/> 基类统一处理。
/// </summary>
public sealed class OpenAIEmbeddingMicroProvider : EmbeddingMicroProvider
{
    /// <summary>通过 <see cref="ProviderEntity"/> 构造 OpenAI Embedding Provider。</summary>
    public OpenAIEmbeddingMicroProvider(ProviderEntityConfig entityConfig, IUsageTracker usageTracker)
        : base(entityConfig, usageTracker)
    {
    }

    /// <inheritdoc />
    protected override IEmbeddingGenerator<string, Embedding<float>> BuildGenerator()
    {
        string endpoint = string.IsNullOrWhiteSpace(Entity.BaseUrl) ? "(OpenAI 默认)" : Entity.BaseUrl;
        Logger.LogDebug(
            "创建 OpenAI Embedding 客户端 — Endpoint: {Endpoint}, Model: {Model}",
            endpoint, Entity.ModelName);

        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(Entity.BaseUrl))
            options.Endpoint = new Uri(Entity.BaseUrl);

        var credential = new ApiKeyCredential(Entity.ApiKey);
        var client = new OpenAIClient(credential, options);

        return client.GetEmbeddingClient(Entity.ModelName).AsIEmbeddingGenerator();
    }
}
