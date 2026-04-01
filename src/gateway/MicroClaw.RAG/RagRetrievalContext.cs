namespace MicroClaw.RAG;

/// <summary>
/// 基于 AsyncLocal 的 scoped 上下文，用于在 RagContextProvider → AgentRunner 之间传递检索到的 chunk 引用列表。
/// 注册为 Singleton，通过 AsyncLocal 实现每个异步流独立的作用域。
/// </summary>
public sealed class RagRetrievalContext
{
    private static readonly AsyncLocal<IReadOnlyList<RagChunkRef>?> _current = new();

    /// <summary>当前异步流中检索到的 RAG 分块引用列表。null 表示本轮未执行 RAG 检索。</summary>
    public IReadOnlyList<RagChunkRef>? RetrievedChunks
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}
