namespace MicroClaw.Configuration;

internal static class MicroClawConfigPathResolver
{
    public static string ResolveFilePath(string configDir, Type optionType, string? fileName, string? directoryPath)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new InvalidOperationException($"配置类型 {optionType.Name} 缺少可落盘的 FileName 元数据。");

        string targetDirectory = string.IsNullOrWhiteSpace(directoryPath) ? configDir : directoryPath;
        return Path.GetFullPath(Path.Combine(targetDirectory, fileName));
    }

    public static string? NormalizeDirectoryPath(Type optionType, string? directoryPath, string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
            return null;

        string trimmedDirectoryPath = directoryPath.Trim();
        string pathToResolve = Path.IsPathRooted(trimmedDirectoryPath)
            ? trimmedDirectoryPath
            : Path.Combine(rootDirectory, trimmedDirectoryPath);

        try
        {
            return Path.GetFullPath(pathToResolve);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException($"配置类型 {optionType.Name} 的 DirectoryPath 无效。", ex);
        }
    }

    public static void EnsureSafeFileName(Type optionType, string fileName)
    {
        string extension = Path.GetExtension(fileName);

        if (Path.IsPathRooted(fileName) || !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || fileName.Contains(':', StringComparison.Ordinal) || fileName.EndsWith(' ') || fileName.EndsWith('.') || (!string.Equals(extension, ".yaml", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".yml", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"配置类型 {optionType.Name} 的 FileName 必须是安全的单个 .yaml/.yml 文件名。");
        }
    }
}