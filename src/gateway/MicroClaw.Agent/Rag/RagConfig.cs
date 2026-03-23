namespace MicroClaw.Agent.Rag;

/// <summary>
/// RAG 知识库配置（预留接口，暂不实现向量存储）
/// </summary>
public sealed record RagConfig(
    string Id,
    string Name,
    RagScope Scope,
    string? SessionId,
    string SourceType,
    bool IsEnabled,
    DateTime CreatedAtUtc
);
