namespace MicroClaw.Agent.Memory;

/// <summary>DNA 基因文件的历史版本快照。</summary>
public sealed record GeneFileSnapshot(
    string SnapshotId,
    string FileName,
    string Category,
    DateTimeOffset SavedAt,
    string Content);
