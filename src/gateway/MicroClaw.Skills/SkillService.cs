namespace MicroClaw.Skills;

/// <summary>
/// Skill 技能文件管理服务。
/// 管理 workspace/skills/{skillId}/ 目录下的脚本文件，防止路径穿越攻击。
/// </summary>
public sealed class SkillService(string workspaceRoot)
{
    private string SkillDirectory(string skillId) =>
        Path.Combine(workspaceRoot, "skills", skillId);

    /// <summary>列出技能目录下所有文件（含子目录，返回相对路径）。</summary>
    public IReadOnlyList<SkillFileInfo> ListFiles(string skillId)
    {
        string dir = SkillDirectory(skillId);
        if (!Directory.Exists(dir)) return [];

        return Directory
            .GetFiles(dir, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                string relativePath = Path.GetRelativePath(dir, path).Replace('\\', '/');
                return new SkillFileInfo(relativePath, new FileInfo(path).Length);
            })
            .OrderBy(f => f.Path)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>读取技能文件内容。</summary>
    public string? GetFile(string skillId, string fileName)
    {
        string safePath = ResolveSafePath(skillId, fileName);
        if (safePath is null || !File.Exists(safePath)) return null;
        return File.ReadAllText(safePath);
    }

    /// <summary>写入技能文件（目录不存在时自动创建）。</summary>
    public void WriteFile(string skillId, string fileName, string content)
    {
        string safePath = ResolveSafePath(skillId, fileName)
            ?? throw new ArgumentException($"Invalid file name: {fileName}");
        Directory.CreateDirectory(Path.GetDirectoryName(safePath)!);
        File.WriteAllText(safePath, content);
    }

    /// <summary>删除技能文件。</summary>
    public bool DeleteFile(string skillId, string fileName)
    {
        string? safePath = ResolveSafePath(skillId, fileName);
        if (safePath is null || !File.Exists(safePath)) return false;
        File.Delete(safePath);
        return true;
    }

    /// <summary>确保技能目录存在。</summary>
    public void EnsureDirectory(string skillId)
    {
        Directory.CreateDirectory(SkillDirectory(skillId));
    }

    /// <summary>删除整个技能目录（技能被删除时调用）。</summary>
    public void DeleteDirectory(string skillId)
    {
        string dir = SkillDirectory(skillId);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
    }

    /// <summary>读取 SKILL.md 内容；若不存在返回 null。</summary>
    public string? GetSkillMd(string skillId) => GetFile(skillId, "SKILL.md");

    /// <summary>检查技能是否为 Playbook 模式（有 SKILL.md）。</summary>
    public bool IsPlaybookMode(string skillId) => GetSkillMd(skillId) is not null;

    /// <summary>读取 tools.json 声明的工具名列表；若不存在或格式错误返回空列表。</summary>
    public IReadOnlyList<string> GetDeclaredTools(string skillId)
    {
        string? json = GetFile(skillId, "tools.json");
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>解析并验证安全路径，阻止路径穿越攻击。若非法则返回 null。</summary>
    private string? ResolveSafePath(string skillId, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return null;

        // 规范化路径分隔符
        string normalized = fileName.Replace('\\', '/');

        // 拒绝绝对路径或包含 ../ 的路径
        if (Path.IsPathRooted(normalized) || normalized.Contains(".."))
            return null;

        string skillDir = Path.GetFullPath(SkillDirectory(skillId));
        string fullPath = Path.GetFullPath(Path.Combine(skillDir, normalized));

        // 确保解析后路径仍在技能目录内
        if (!fullPath.StartsWith(skillDir + Path.DirectorySeparatorChar)
            && fullPath != skillDir)
            return null;

        return fullPath;
    }
}

public sealed record SkillFileInfo(string Path, long SizeBytes);
