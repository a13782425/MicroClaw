namespace MicroClaw.Agent.Memory;

/// <summary>DNA 基因文件记录，对应 agents/{agentId}/dna/ 目录下的一个 Markdown 文件。</summary>
public sealed record GeneFile(
    string FileName,
    string Category,
    string Content,
    DateTimeOffset UpdatedAt);
