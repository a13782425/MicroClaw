
namespace MicroClaw.Configuration;

/// <summary>
/// 文件操作工具的限额配置。沙箱目录由 FileToolProvider 根据 sessionId 自动计算。
/// 通过配置节 "filesystem" 绑定。
/// </summary>
[MicroClawYamlConfig("filesystem", FileName = "filesystem.yaml")]
public sealed class FileToolsOptions : IMicroClawConfigTemplate
{
    /// <summary>单次读取返回的最大字符数（默认 100,000）。</summary>
    [YamlMember(Alias = "max_read_chars", Description = "单次读取允许返回的最大字符数。")]
    public int MaxReadChars { get; set; } = 100_000;

    /// <summary>单次写入的最大字节数（默认 10 MB）。</summary>
    [YamlMember(Alias = "max_file_write_bytes", Description = "单次写入允许的最大字节数。")]
    public long MaxFileWriteBytes { get; set; } = 10_000_000;

    public IMicroClawConfigOptions CreateDefaultTemplate() => new FileToolsOptions();
}
