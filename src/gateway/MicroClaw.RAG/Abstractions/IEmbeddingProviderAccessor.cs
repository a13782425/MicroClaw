namespace MicroClaw.RAG;

/// <summary>
/// 提供对当前可用 Embedding 服务的动态访问。主项目实现此接口，以解耦 RAG 对 Providers 的直接依赖。
/// </summary>
public interface IEmbeddingProviderAccessor
{
    /// <summary>
    /// 返回当前已启用的 <see cref="IEmbeddingService"/>；若无可用 Provider 则返回 <c>null</c>。
    /// 每次调用均实时读取配置，支持运行时热切换。
    /// </summary>
    IEmbeddingService? GetCurrentService();
}
