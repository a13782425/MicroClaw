using Microsoft.Extensions.Configuration;

namespace MicroClaw.Configuration;

/// <summary>
/// 文件操作工具的限额配置。沙箱目录由 FileToolProvider 根据 sessionId 自动计算。
/// 通过配置节 "filesystem" 绑定。
/// </summary>
[MicroClawYamlConfig("filesystem")]
public sealed class FileToolsOptions : IMicroClawConfigOptions
{
    /// <summary>单次读取返回的最大字符数（默认 100,000）。</summary>
    [ConfigurationKeyName("max_read_chars")]
    public int MaxReadChars { get; set; } = 100_000;

    /// <summary>单次写入的最大字节数（默认 10 MB）。</summary>
    [ConfigurationKeyName("max_file_write_bytes")]
    public long MaxFileWriteBytes { get; set; } = 10_000_000;
}
