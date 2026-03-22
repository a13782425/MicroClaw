namespace MicroClaw.Agent.Memory;

/// <summary>DNA 从 Markdown 导入操作的单文件结果。</summary>
public sealed record DnaImportEntryResult(
    string FileName,
    string Category,
    bool Success,
    string? Error);
