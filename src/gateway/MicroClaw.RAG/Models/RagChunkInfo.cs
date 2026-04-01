namespace MicroClaw.RAG;

/// <summary>
/// RAG 分块摘要信息，供前端管理界面展示和操作。
/// </summary>
/// <param name="Id">分块唯一标识。</param>
/// <param name="SourceId">来源标识（分类名、文档 ID 等）。</param>
/// <param name="Content">分块原始文本内容。</param>
/// <param name="HitCount">被检索命中的累计次数（经 AI 审计确认的真实使用次数）。</param>
/// <param name="CreatedAtMs">创建时间（Unix 毫秒）。</param>
/// <param name="LastAccessedAtMs">最近一次被检索命中时间（Unix 毫秒），null 表示从未命中。</param>
public sealed record RagChunkInfo(
    string Id,
    string SourceId,
    string Content,
    int HitCount,
    long CreatedAtMs,
    long? LastAccessedAtMs);
