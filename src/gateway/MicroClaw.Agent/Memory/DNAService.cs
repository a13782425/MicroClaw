using System.Text;

namespace MicroClaw.Agent.Memory;

/// <summary>
/// DNA 基因文件服务：管理 agents/{agentId}/dna/ 目录下的 Markdown 记忆文件。
/// 支持按 Category（子目录）组织，提供 CRUD 和 SystemPrompt 注入。
/// </summary>
public sealed class DNAService(string agentsDataDir)
{
    private string AgentDir(string agentId) => Path.Combine(agentsDataDir, agentId, "dna");

    public IReadOnlyList<GeneFile> List(string agentId)
    {
        string dir = AgentDir(agentId);
        if (!Directory.Exists(dir)) return [];

        return Directory.EnumerateFiles(dir, "*.md", SearchOption.AllDirectories)
            .Select(filePath =>
            {
                string relative = Path.GetRelativePath(dir, filePath);
                string category = Path.GetDirectoryName(relative)?.Replace('\\', '/') ?? string.Empty;
                string fileName = Path.GetFileName(filePath);
                string content = File.ReadAllText(filePath);
                DateTimeOffset updatedAt = File.GetLastWriteTimeUtc(filePath);
                return new GeneFile(fileName, category, content, updatedAt);
            })
            .ToList()
            .AsReadOnly();
    }

    public GeneFile? Get(string agentId, string category, string fileName)
    {
        string filePath = BuildFilePath(agentId, category, fileName);
        if (!File.Exists(filePath)) return null;

        string content = File.ReadAllText(filePath);
        DateTimeOffset updatedAt = File.GetLastWriteTimeUtc(filePath);
        return new GeneFile(fileName, category, content, updatedAt);
    }

    public GeneFile Write(string agentId, string category, string fileName, string content)
    {
        string filePath = BuildFilePath(agentId, category, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, content);
        DateTimeOffset updatedAt = File.GetLastWriteTimeUtc(filePath);
        return new GeneFile(fileName, category, content, updatedAt);
    }

    public bool Delete(string agentId, string category, string fileName)
    {
        string filePath = BuildFilePath(agentId, category, fileName);
        if (!File.Exists(filePath)) return false;
        File.Delete(filePath);
        return true;
    }

    /// <summary>将所有基因文件拼接为可注入 SystemPrompt 的 Markdown 字符串。</summary>
    public string BuildSystemPromptContext(string agentId)
    {
        IReadOnlyList<GeneFile> files = List(agentId);
        if (files.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## DNA 记忆");
        foreach (GeneFile gene in files)
        {
            string label = string.IsNullOrWhiteSpace(gene.Category)
                ? gene.FileName
                : $"{gene.Category}/{gene.FileName}";
            sb.AppendLine($"### {label}");
            sb.AppendLine(gene.Content);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private string BuildFilePath(string agentId, string category, string fileName)
    {
        string dir = AgentDir(agentId);
        if (!string.IsNullOrWhiteSpace(category))
            dir = Path.Combine(dir, category);
        return Path.Combine(dir, fileName);
    }
}
