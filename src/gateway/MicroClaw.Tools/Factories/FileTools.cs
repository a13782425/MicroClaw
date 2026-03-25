using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace MicroClaw.Tools;

/// <summary>
/// 文件操作 AI 工具工厂：生成 read_file、write_file、edit_file、list_directory、search_files 五个函数，
/// 使 AI Agent 能在白名单目录内读写编辑本地文件。
/// 所有路径操作均经过规范化和白名单校验，防止路径穿越攻击。
/// </summary>
public static class FileTools
{
    private const int MaxSearchResults = 100;
    private const int MaxSearchFiles = 1000;
    private const int MaxSearchLinesPerFile = 10_000;

    private static readonly IReadOnlyList<(string Name, string Description)> BuiltinToolDescriptions =
    [
        ("read_file", "Read file contents with optional line range"),
        ("write_file", "Create or overwrite a file"),
        ("edit_file", "Search and replace text in a file"),
        ("list_directory", "List directory contents"),
        ("search_files", "Search for text or regex in files"),
    ];

    public static IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        BuiltinToolDescriptions;

    /// <summary>
    /// 创建文件操作工具列表。所有路径操作限制在 <paramref name="sandboxDir"/> 内。
    /// 支持相对路径（自动解析到沙箱目录）。
    /// </summary>
    public static IReadOnlyList<AIFunction> Create(string sandboxDir, FileToolsOptions options)
    {
        string normalizedSandbox = NormalizeSandboxDir(sandboxDir);
        IReadOnlyList<string> allowedDirs = [normalizedSandbox];
        int maxReadChars = options.MaxReadChars;
        long maxWriteBytes = options.MaxFileWriteBytes;

        return
        [
            CreateReadFile(normalizedSandbox, allowedDirs, maxReadChars),
            CreateWriteFile(normalizedSandbox, allowedDirs, maxWriteBytes),
            CreateEditFile(normalizedSandbox, allowedDirs),
            CreateListDirectory(normalizedSandbox, allowedDirs),
            CreateSearchFiles(normalizedSandbox, allowedDirs),
        ];
    }

    // ── read_file ────────────────────────────────────────────────────────────

    private static AIFunction CreateReadFile(string sandboxDir, IReadOnlyList<string> allowedDirs, int maxReadChars)
    {
        return AIFunctionFactory.Create(
            async (
                [Description("文件路径（相对于工作目录，或绝对路径）")] string filePath,
                [Description("起始行号（1-based，可选）。省略则从头开始")] int? startLine = null,
                [Description("结束行号（1-based，包含，可选）。省略则读到末尾")] int? endLine = null) =>
            {
                filePath = ResolvePath(filePath, sandboxDir);
                string? error = ValidatePath(filePath, allowedDirs, mustExist: true);
                if (error is not null) return (object)new { success = false, error };

                string fullPath = Path.GetFullPath(filePath);

                // 二进制文件检测：读取前 8KB 检查 null 字节
                byte[] header = new byte[8192];
                int headerLen;
                await using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    headerLen = await fs.ReadAsync(header.AsMemory(0, header.Length));
                }

                if (IsBinary(header.AsSpan(0, headerLen)))
                    return (object)new { success = false, error = "该文件是二进制文件，无法作为文本读取" };

                string[] lines = await File.ReadAllLinesAsync(fullPath, Encoding.UTF8);
                int totalLines = lines.Length;

                int from = Math.Max(1, startLine ?? 1);
                int to = Math.Min(totalLines, endLine ?? totalLines);

                if (from > totalLines)
                    return (object)new { success = true, filePath = fullPath, content = "", totalLines, note = $"startLine ({from}) 超出文件总行数 ({totalLines})" };

                var selected = lines[(from - 1)..to];
                string content = string.Join('\n', selected);

                bool truncated = false;
                if (content.Length > maxReadChars)
                {
                    content = content[..maxReadChars];
                    truncated = true;
                }

                return new
                {
                    success = true,
                    filePath = fullPath,
                    content,
                    startLine = from,
                    endLine = to,
                    totalLines,
                    truncated,
                };
            },
            name: "read_file",
            description: $"读取指定文件的文本内容。支持通过 startLine/endLine 指定行范围（1-based）。大文件建议分段读取。当前工作目录：{sandboxDir}");
    }

    // ── write_file ───────────────────────────────────────────────────────────

    private static AIFunction CreateWriteFile(string sandboxDir, IReadOnlyList<string> allowedDirs, long maxWriteBytes)
    {
        return AIFunctionFactory.Create(
            async (
                [Description("文件路径（相对于工作目录，或绝对路径）。若文件已存在则覆盖，不存在则创建（含中间目录）")] string filePath,
                [Description("要写入的文件内容（UTF-8 文本）")] string content) =>
            {
                filePath = ResolvePath(filePath, sandboxDir);
                string? error = ValidatePath(filePath, allowedDirs, mustExist: false);
                if (error is not null) return (object)new { success = false, error };

                string fullPath = Path.GetFullPath(filePath);
                int byteCount = Encoding.UTF8.GetByteCount(content);
                if (byteCount > maxWriteBytes)
                    return (object)new { success = false, error = $"内容大小 ({byteCount} 字节) 超出单次写入上限 ({maxWriteBytes} 字节)" };

                try
                {
                    string? dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8);

                    return new
                    {
                        success = true,
                        filePath = fullPath,
                        bytesWritten = byteCount,
                    };
                }
                catch (Exception ex)
                {
                    return (object)new { success = false, error = ex.Message };
                }
            },
            name: "write_file",
            description: $"创建或覆盖指定文件。自动创建中间目录。内容以 UTF-8 编码写入。当前工作目录：{sandboxDir}");
    }

    // ── edit_file ────────────────────────────────────────────────────────────

    private static AIFunction CreateEditFile(string sandboxDir, IReadOnlyList<string> allowedDirs)
    {
        return AIFunctionFactory.Create(
            async (
                [Description("文件路径（相对于工作目录，或绝对路径）")] string filePath,
                [Description("要替换的原始文本（必须与文件中的内容完全一致，且在文件中只出现一次）")] string oldText,
                [Description("用于替换的新文本")] string newText) =>
            {
                filePath = ResolvePath(filePath, sandboxDir);
                string? error = ValidatePath(filePath, allowedDirs, mustExist: true);
                if (error is not null) return (object)new { success = false, error };

                string fullPath = Path.GetFullPath(filePath);
                string original = await File.ReadAllTextAsync(fullPath, Encoding.UTF8);

                int firstIdx = original.IndexOf(oldText, StringComparison.Ordinal);
                if (firstIdx < 0)
                    return (object)new { success = false, error = "oldText 在文件中未找到，请检查文本是否完全匹配（含空格和换行）" };

                int secondIdx = original.IndexOf(oldText, firstIdx + 1, StringComparison.Ordinal);
                if (secondIdx >= 0)
                    return (object)new { success = false, error = "oldText 在文件中匹配了多处，请提供更多上下文使其唯一" };

                string updated = string.Concat(
                    original.AsSpan(0, firstIdx),
                    newText,
                    original.AsSpan(firstIdx + oldText.Length));

                await File.WriteAllTextAsync(fullPath, updated, Encoding.UTF8);

                return new
                {
                    success = true,
                    filePath = fullPath,
                    replacements = 1,
                };
            },
            name: "edit_file",
            description: $"通过精确搜索替换编辑文件。oldText 必须与文件内容完全匹配且只出现一次。包含足够的上下文行以确保唯一匹配。当前工作目录：{sandboxDir}");
    }

    // ── list_directory ───────────────────────────────────────────────────────

    private static AIFunction CreateListDirectory(string sandboxDir, IReadOnlyList<string> allowedDirs)
    {
        return AIFunctionFactory.Create(
            (
                [Description("目录路径（相对于工作目录，或绝对路径）。省略则列出工作目录")] string? directoryPath = null,
                [Description("是否递归列出子目录内容（默认 false，递归最深 3 层）")] bool recursive = false) =>
            {
                directoryPath = ResolvePath(directoryPath ?? ".", sandboxDir);
                string? error = ValidatePath(directoryPath, allowedDirs, mustExist: true, isDirectory: true);
                if (error is not null) return (object)new { success = false, error };

                string fullPath = Path.GetFullPath(directoryPath);

                try
                {
                    List<string> entries = [];
                    CollectEntries(fullPath, fullPath, entries, recursive, maxDepth: 3, currentDepth: 0);

                    return new
                    {
                        success = true,
                        directoryPath = fullPath,
                        entries = entries.AsReadOnly(),
                        totalEntries = entries.Count,
                    };
                }
                catch (Exception ex)
                {
                    return (object)new { success = false, error = ex.Message };
                }
            },
            name: "list_directory",
            description: $"列出指定目录的文件和子目录。目录名以 / 结尾。支持 recursive 参数递归列出（最深 3 层）。当前工作目录：{sandboxDir}");
    }

    // ── search_files ─────────────────────────────────────────────────────────

    private static AIFunction CreateSearchFiles(string sandboxDir, IReadOnlyList<string> allowedDirs)
    {
        return AIFunctionFactory.Create(
            async (
                [Description("目录路径（相对于工作目录，或绝对路径）。省略则搜索工作目录")] string? directory = null,
                [Description("搜索文本或正则表达式模式")] string pattern = "",
                [Description("pattern 是否为正则表达式（默认 false，即纯文本搜索）")] bool isRegex = false,
                [Description("文件名 glob 过滤（如 *.cs、*.txt），默认搜索所有文件")] string? filePattern = null) =>
            {
                directory = ResolvePath(directory ?? ".", sandboxDir);
                string? error = ValidatePath(directory, allowedDirs, mustExist: true, isDirectory: true);
                if (error is not null) return (object)new { success = false, error };

                string fullDir = Path.GetFullPath(directory);

                Regex? regex = null;
                if (isRegex)
                {
                    try
                    {
                        regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(5));
                    }
                    catch (RegexParseException ex)
                    {
                        return (object)new { success = false, error = $"正则表达式无效：{ex.Message}" };
                    }
                }

                try
                {
                    string searchPattern = filePattern ?? "*";
                    IEnumerable<string> files = Directory.EnumerateFiles(fullDir, searchPattern, SearchOption.AllDirectories);

                    List<object> matches = [];
                    int filesScanned = 0;
                    bool limitReached = false;

                    foreach (string file in files)
                    {
                        if (filesScanned >= MaxSearchFiles) { limitReached = true; break; }
                        filesScanned++;

                        int lineNum = 0;
                        await foreach (string line in ReadLinesAsync(file))
                        {
                            lineNum++;
                            if (lineNum > MaxSearchLinesPerFile) break;

                            bool matched = regex is not null
                                ? regex.IsMatch(line)
                                : line.Contains(pattern, StringComparison.OrdinalIgnoreCase);

                            if (matched)
                            {
                                matches.Add(new { file, line = lineNum, text = Truncate(line.TrimEnd(), 200) });
                                if (matches.Count >= MaxSearchResults) { limitReached = true; break; }
                            }
                        }

                        if (limitReached) break;
                    }

                    return new
                    {
                        success = true,
                        directory = fullDir,
                        pattern,
                        matches,
                        totalMatches = matches.Count,
                        filesScanned,
                        limitReached,
                    };
                }
                catch (Exception ex)
                {
                    return (object)new { success = false, error = ex.Message };
                }
            },
            name: "search_files",
            description: $"在指定目录中搜索包含特定文本或正则表达式的文件行。返回匹配的文件路径、行号和行内容。支持 filePattern 过滤文件类型。当前工作目录：{sandboxDir}");
    }

    // ── 路径安全校验 ─────────────────────────────────────────────────────────

    internal static string? ValidatePath(string path, IReadOnlyList<string> allowedDirs, bool mustExist, bool isDirectory = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "路径不能为空";

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch (Exception ex) { return $"路径无效：{ex.Message}"; }

        // 白名单检查（允许路径位于白名单目录内，或路径本身就是白名单目录）
        bool allowed = false;
        foreach (string dir in allowedDirs)
        {
            if (fullPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fullPath + Path.DirectorySeparatorChar, dir, StringComparison.OrdinalIgnoreCase))
            {
                allowed = true;
                break;
            }
        }

        if (!allowed)
            return $"路径 '{fullPath}' 不在允许的目录范围内。允许的目录：{string.Join(", ", allowedDirs)}";

        if (mustExist)
        {
            if (isDirectory)
            {
                if (!Directory.Exists(fullPath))
                    return $"目录不存在：{fullPath}";
            }
            else
            {
                if (!File.Exists(fullPath))
                    return $"文件不存在：{fullPath}";
            }
        }

        // 符号链接检测：确保解析后的真实路径也在白名单内
        try
        {
            string? resolvedPath = isDirectory || (!mustExist && !File.Exists(fullPath))
                ? null
                : Path.GetFullPath(new FileInfo(fullPath).LinkTarget ?? fullPath);

            if (resolvedPath is not null && resolvedPath != fullPath)
            {
                bool resolvedAllowed = false;
                foreach (string dir in allowedDirs)
                {
                    if (resolvedPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
                    {
                        resolvedAllowed = true;
                        break;
                    }
                }

                if (!resolvedAllowed)
                    return $"符号链接的目标 '{resolvedPath}' 不在允许的目录范围内";
            }
        }
        catch
        {
            // 符号链接解析失败不阻塞操作
        }

        return null;
    }

    // ── 辅助方法 ─────────────────────────────────────────────────────────────

    private static string NormalizeSandboxDir(string dir)
    {
        string full = Path.GetFullPath(dir);
        // 确保以目录分隔符结尾，防止前缀误匹配（如 /tmp/a 匹配 /tmp/abc）
        if (!full.EndsWith(Path.DirectorySeparatorChar))
            full += Path.DirectorySeparatorChar;
        return full;
    }

    private static string ResolvePath(string path, string sandboxDir) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(sandboxDir, path));

    private static bool IsBinary(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
        {
            if (b == 0) return true;
        }

        return false;
    }

    private static void CollectEntries(string rootPath, string currentPath, List<string> entries, bool recursive, int maxDepth, int currentDepth)
    {
        try
        {
            foreach (string dir in Directory.GetDirectories(currentPath))
            {
                string relative = Path.GetRelativePath(rootPath, dir).Replace('\\', '/') + "/";
                entries.Add(relative);

                if (recursive && currentDepth < maxDepth)
                    CollectEntries(rootPath, dir, entries, recursive, maxDepth, currentDepth + 1);
            }

            foreach (string file in Directory.GetFiles(currentPath))
            {
                string relative = Path.GetRelativePath(rootPath, file).Replace('\\', '/');
                entries.Add(relative);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // 忽略无权限的目录
        }
    }

    private static async IAsyncEnumerable<string> ReadLinesAsync(string filePath)
    {
        using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (await reader.ReadLineAsync() is { } line)
            yield return line;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "...";
}
