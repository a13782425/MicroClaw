using System.ClientModel;
using MicroClaw.Common;
using MicroClaw.Configuration;
using MicroClaw.Core.Logging;
using Microsoft.Extensions.AI;
using OpenAI;

namespace MicroClaw.Providers.OpenAI;

/// <summary>
/// OpenAI 协议的 Embedding Provider。批量调用与 usage 追踪由 <see cref="EmbeddingModelClient"/> 基类处理。
/// </summary>
public sealed class OpenAIEmbeddingModelClient : EmbeddingModelClient
{
    public OpenAIEmbeddingModelClient(ProviderEntityConfig config, IUsageTracker usageTracker)
        : base(config, usageTracker) { }

    /// <inheritdoc />
    protected override IEmbeddingGenerator<string, Embedding<float>> BuildGenerator()
    {
        string endpoint = string.IsNullOrWhiteSpace(BaseUrl) ? "(OpenAI 默认)" : BaseUrl!;
        Logger.LogDebug("创建 OpenAI Embedding 客户端 — Endpoint: {Endpoint}, Model: {Model}",
            endpoint, ModelName);

        var options = new OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            options.Endpoint = new Uri(BaseUrl);

        var credential = new ApiKeyCredential(ApiKey);
        var client = new OpenAIClient(credential, options);

        return client.GetEmbeddingClient(ModelName).AsIEmbeddingGenerator();
    }
}
