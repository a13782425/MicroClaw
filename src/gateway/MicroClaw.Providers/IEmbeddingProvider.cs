using Microsoft.Extensions.AI;

namespace MicroClaw.Providers;

/// <summary>
/// 嵌入模型提供者接口 — 按 <see cref="ProviderProtocol"/> 创建 <see cref="IEmbeddingGenerator{String, Embedding}"/>。
/// </summary>
public interface IEmbeddingProvider
{
    bool Supports(ProviderProtocol protocol);
    IEmbeddingGenerator<string, Embedding<float>> Create(ProviderConfig config);
}
