namespace MicroClaw.Skills;

/// <summary>
/// Skill 技能文件管理服务。
/// 管理一个或多个技能文件夹（skillRoots）下的 {skillId}/ 目录，防止路径穿越攻击。
/// 第一个 root（DefaultSkillRoot）为新技能的写入目录；其余 root 仅用于读取和扫描。
/// </summary>
public sealed class SkillService
{
    /// <summary>返回 workspace 根目录路径。</summary>
    public string WorkspaceRoot { get; }

    /// <summary>所有技能文件夹的绝对路径列表（首个为默认写入目录）。</summary>
    public IReadOnlyList<string> SkillRoots { get; }

    /// <summary>新技能的默认写入目录（SkillRoots[0]）。</summary>
    public string DefaultSkillRoot => SkillRoots[0];

    /// <summary>SkillManifest 文件级缓存：key=skillId, value=(文件最后修改时间, 解析结果)。</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime LastWrite, SkillManifest Manifest)> _manifestCache = new();

    public SkillService(string workspaceRoot, IReadOnlyList<string>? skillRoots = null)
    {
        WorkspaceRoot = workspaceRoot;
        if (skillRoots is { Count: > 0 })
        {
            SkillRoots = skillRoots;
        }
        else
        {
            // 默认使用 {workspaceRoot}/skills/
            SkillRoots = [Path.GetFullPath(Path.Combine(workspaceRoot, "skills"))];
        }
    }

    /// <summary>
    /// 返回指定 skillId 的技能目录完整路径。
    /// 按顺序遍历 SkillRoots，返回第一个已存在该子目录的路径；
    /// 若均不存在，则返回默认 root（SkillRoots[0]）下的路径（供新建使用）。
    /// </summary>
    private string SkillDirectory(string skillId)
    {
        foreach (string root in SkillRoots)
        {
            string candidate = Path.GetFullPath(Path.Combine(root, skillId));
            if (Directory.Exists(candidate))
                return candidate;
        }
        return Path.GetFullPath(Path.Combine(DefaultSkillRoot, skillId));
    }

    /// <summary>返回技能的工作目录完整路径（供 ${CLAUDE_SKILL_DIR} 替换使用）。</summary>
    public string GetSkillDirectory(string skillId) => SkillDirectory(skillId);

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
        string? safePath = ResolveSafePath(skillId, fileName);
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
        _manifestCache.TryRemove(skillId, out _);
    }

    /// <summary>删除技能文件。</summary>
    public bool DeleteFile(string skillId, string fileName)
    {
        string? safePath = ResolveSafePath(skillId, fileName);
        if (safePath is null || !File.Exists(safePath)) return false;
        File.Delete(safePath);
        _manifestCache.TryRemove(skillId, out _);
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
        _manifestCache.TryRemove(skillId, out _);
    }

    /// <summary>读取 SKILL.md 内容；若不存在返回 null。</summary>
    public string? GetSkillMd(string skillId) => GetFile(skillId, "SKILL.md");

    /// <summary>
    /// 在技能指令文本中执行 !`command` 语法的 shell 命令注入。
    /// 每个 !`command` 占位符将被替换为命令的标准输出。
    /// 若未启用命令注入，则移除占位符（以空字符串替换）。
    /// 命令工作目录固定为 skillDir（防止路径逃逸）。
    /// 单条命令超时限制为 30 秒。
    /// </summary>
    public string ApplyCommandInjections(string text, string skillDir)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            text,
            @"!\`([^`]+)\`",
            m =>
            {
                string command = m.Groups[1].Value;
                try
                {
                    // 统一使用 sh -c 或 cmd /c，根据操作系统选择
                    bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                        System.Runtime.InteropServices.OSPlatform.Windows);

                    using var proc = new System.Diagnostics.Process();
                    proc.StartInfo = isWindows
                        ? new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {command}")
                        : new System.Diagnostics.ProcessStartInfo("/bin/sh", $"-c \"{command.Replace("\"", "\\\"")}\"");

                    proc.StartInfo.WorkingDirectory = skillDir;
                    proc.StartInfo.RedirectStandardOutput = true;
                    proc.StartInfo.RedirectStandardError = true;
                    proc.StartInfo.UseShellExecute = false;
                    proc.StartInfo.CreateNoWindow = true;

                    proc.Start();
                    bool completed = proc.WaitForExit(30_000); // 30s 超时
                    if (!completed)
                    {
                        proc.Kill(entireProcessTree: true);
                        return $"[command timed out: {command}]";
                    }

                    return proc.StandardOutput.ReadToEnd().TrimEnd('\r', '\n');
                }
                catch (Exception ex)
                {
                    return $"[command error: {ex.Message}]";
                }
            });
    }

    /// <summary>
    /// 解析 SKILL.md frontmatter，返回 <see cref="SkillManifest"/>。
    /// 带文件时间戳缓存：若文件未修改则直接返回缓存值，避免重复磁盘 IO 和解析。
    /// </summary>
    public SkillManifest ParseManifest(string skillId)
    {
        string skillMdPath = Path.Combine(Path.GetFullPath(SkillDirectory(skillId)), "SKILL.md");

        if (!File.Exists(skillMdPath))
        {
            _manifestCache.TryRemove(skillId, out _);
            return SkillManifest.Fallback;
        }

        DateTime lastWrite = File.GetLastWriteTimeUtc(skillMdPath);

        if (_manifestCache.TryGetValue(skillId, out var cached) && cached.LastWrite == lastWrite)
            return cached.Manifest;

        SkillManifest manifest = SkillManifest.Parse(File.ReadAllText(skillMdPath));
        _manifestCache[skillId] = (lastWrite, manifest);
        return manifest;
    }

    /// <summary>
    /// 执行单条 shell 命令，返回结构化结果（ExitCode + Stdout + Stderr）。
    /// 用于 hooks 执行场景，区别于 ApplyCommandInjections 的 inline 替换。
    /// </summary>
    public CommandResult ExecuteCommand(string command, string workDir, int timeoutSeconds = 30)
    {
        bool isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows);

        using var proc = new System.Diagnostics.Process();
        proc.StartInfo = isWindows
            ? new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {command}")
            : new System.Diagnostics.ProcessStartInfo("/bin/sh", $"-c \"{command.Replace("\"", "\\\"")}\"");

        proc.StartInfo.WorkingDirectory = workDir;
        proc.StartInfo.RedirectStandardOutput = true;
        proc.StartInfo.RedirectStandardError = true;
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.CreateNoWindow = true;

        proc.Start();
        bool completed = proc.WaitForExit(timeoutSeconds * 1000);
        if (!completed)
        {
            proc.Kill(entireProcessTree: true);
            return new CommandResult(-1, string.Empty, $"command timed out after {timeoutSeconds}s");
        }

        return new CommandResult(
            proc.ExitCode,
            proc.StandardOutput.ReadToEnd().TrimEnd('\r', '\n'),
            proc.StandardError.ReadToEnd().TrimEnd('\r', '\n'));
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

/// <summary>命令执行结构化结果。</summary>
public sealed record CommandResult(int ExitCode, string Stdout, string Stderr);
