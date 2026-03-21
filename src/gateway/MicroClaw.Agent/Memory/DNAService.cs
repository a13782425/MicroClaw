using System.Text;

namespace MicroClaw.Agent.Memory;

/// <summary>
/// DNA 基因文件服务：管理 agents/{agentId}/dna/ 目录下的 Markdown 记忆文件。
/// 支持按 Category（子目录）组织，提供 CRUD 和 SystemPrompt 注入。
/// </summary>
public sealed class DNAService(string agentsDataDir)
{
    /// <summary>单次注入 SystemPrompt 的 DNA 上下文最大字节数（50 KB）。</summary>
    public const int MaxDnaSizeBytes = 50 * 1024;

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

    /// <summary>将所有基因文件拼接为可注入 SystemPrompt 的 Markdown 字符串。
    /// 总大小超过 <see cref="MaxDnaSizeBytes"/>（50KB）时按列举顺序截取，优先保留靠前的文件。</summary>
    public string BuildSystemPromptContext(string agentId)
    {
        IReadOnlyList<GeneFile> files = List(agentId);
        if (files.Count == 0) return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("## DNA 记忆");
        int included = 0;

        foreach (GeneFile gene in files)
        {
            string label = string.IsNullOrWhiteSpace(gene.Category)
                ? gene.FileName
                : $"{gene.Category}/{gene.FileName}";
            string section = $"### {label}\n{gene.Content}\n\n";

            // 超出大小上限时停止追加，优先保留已包含的文件
            if (Encoding.UTF8.GetByteCount(sb.ToString()) + Encoding.UTF8.GetByteCount(section) > MaxDnaSizeBytes)
                break;

            sb.Append(section);
            included++;
        }

        if (included < files.Count)
            sb.AppendLine($"\n> ⚠️ 因 DNA 大小超过 50 KB 上限，仅注入 {included}/{files.Count} 个基因文件，剩余文件已跳过。");

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
