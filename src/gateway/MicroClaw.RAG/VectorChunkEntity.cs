namespace MicroClaw.RAG;

/// <summary>
/// RAG 向量分块实体，存储文本片段及其嵌入向量。
/// </summary>
public sealed class VectorChunkEntity
{
    /// <summary>分块唯一标识（GUID）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>来源标识（文档 ID、会话 ID、DNA 文件路径等）。</summary>
    public string SourceId { get; set; } = string.Empty;

    /// <summary>分块的原始文本内容。</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>嵌入向量，float[] 序列化为 byte[]（小端 IEEE 754）。</summary>
    public byte[] VectorBlob { get; set; } = [];

    /// <summary>扩展元数据 JSON（文件名、分块索引、标题层级等）。</summary>
    public string? MetadataJson { get; set; }

    /// <summary>创建时间：相对于 TimeBase.BaseTime 的毫秒偏移。</summary>
    public long CreatedAtMs { get; set; }
}
