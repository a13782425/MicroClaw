namespace MicroClaw.Agent.Rag;

/// <summary>
/// RAG 服务接口（预留，向量存储实现待定）
/// </summary>
public interface IRagService
{
    /// <summary>查询 RAG 知识库</summary>
    Task<string> QueryAsync(string query, RagScope scope, string? sessionId, CancellationToken ct = default);

    /// <summary>向 RAG 知识库注入数据</summary>
    Task IngestAsync(string source, RagScope scope, string? sessionId, CancellationToken ct = default);
}
