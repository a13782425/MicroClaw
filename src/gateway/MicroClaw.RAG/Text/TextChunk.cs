namespace MicroClaw.RAG;

/// <summary>
/// 文本分块结果。
/// </summary>
/// <param name="Index">分块在原始文档中的序号（从 0 开始）。</param>
/// <param name="Content">分块的文本内容。</param>
/// <param name="TokenCount">分块的 token 数量。</param>
public sealed record TextChunk(int Index, string Content, int TokenCount);
