namespace MicroClaw.Configuration;

public sealed class ConfigurationConflictException : Exception
{
    public string FileA { get; }
    public string FileB { get; }
    public IReadOnlyList<string> ConflictingKeys { get; }

    public ConfigurationConflictException(string fileA, string fileB, IReadOnlyList<string> conflictingKeys)
        : base(BuildMessage(fileA, fileB, conflictingKeys))
    {
        FileA = fileA;
        FileB = fileB;
        ConflictingKeys = conflictingKeys;
    }

    private static string BuildMessage(string fileA, string fileB, IReadOnlyList<string> keys)
    {
        var keyList = string.Join("\n    ", keys.Select(k => $"\"{k}\""));
        return $"""
            配置冲突：以下 key 在两个导入文件中重复定义，请确保每个配置键只在一个文件中出现。
              文件 A: {fileA}
              文件 B: {fileB}
              重复的 key:
                {keyList}
            """;
    }
}
