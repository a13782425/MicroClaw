namespace MicroClaw.RAG;

/// <summary>
/// RAG 使用审计接口。在一轮对话完成后，通过 AI 比对检索到的 chunk 与 AI 实际回复，
/// 精确判定哪些 chunk 真正被使用，仅对这些 chunk 增加 HitCount。
/// </summary>
public interface IRagUsageAuditor
{
    /// <summary>
    /// 审计一轮对话中 RAG 检索结果的实际使用情况。
    /// 对确认被 AI 使用的 chunk 执行 HitCount +1。
    /// </summary>
    Task AuditAsync(
        IReadOnlyList<RagChunkRef> retrievedChunks,
        string assistantResponse,
        CancellationToken ct = default);
}
