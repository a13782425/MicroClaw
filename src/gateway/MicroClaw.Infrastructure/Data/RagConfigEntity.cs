namespace MicroClaw.Infrastructure.Data;

/// <summary>
/// RAG 知识库配置的 EF Core 实体（预留）
/// </summary>
public sealed class RagConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>"Global" 或 "Session"</summary>
    public string Scope { get; set; } = "Global";

    public string? SessionId { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    /// <summary>创建时间：相对于 TimeBase.BaseTime 的毫秒偏移。</summary>
    public long CreatedAtMs { get; set; }
}
