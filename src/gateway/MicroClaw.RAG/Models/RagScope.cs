namespace MicroClaw.RAG;

/// <summary>
/// RAG 知识库的作用域
/// </summary>
public enum RagScope
{
    /// <summary>全局知识库，所有会话共享</summary>
    Global,

    /// <summary>会话私有知识库</summary>
    Session,
}
