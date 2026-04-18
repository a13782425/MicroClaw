using System.ClientModel;
using MicroClaw.Core.Logging;
using MicroClaw.Infrastructure.Data;
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
    /// <summary>通过 <see cref="ProviderConfig"/> 构造 OpenAI Embedding Provider。</summary>
    public OpenAIEmbeddingMicroProvider(ProviderConfig config, IUsageTracker usageTracker)
        : base(config, usageTracker)
    {
    }

    /// <inheritdoc />
    protected override IEmbeddingGenerator<string, Embedding<float>> BuildGenerator()
    {
        string endpoint = string.IsNullOrWhiteSpace(Config.BaseUrl) ? "(OpenAI 默认)" : Config.BaseUrl;
        Logger.LogDebug(
            "创建 OpenAI Embedding 客户端 — Endpoint: {Endpoint}, Model: {Model}",
            endpoint, Config.ModelName);

        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(Config.BaseUrl))
            options.Endpoint = new Uri(Config.BaseUrl);

        var credential = new ApiKeyCredential(Config.ApiKey);
        var client = new OpenAIClient(credential, options);

        return client.GetEmbeddingClient(Config.ModelName).AsIEmbeddingGenerator();
    }
}
