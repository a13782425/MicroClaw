namespace MicroClaw.Infrastructure.Data;

public sealed class McpServerConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>传输类型："stdio" 或 "sse"</summary>
    public string TransportType { get; set; } = "stdio";
    /// <summary>stdio 传输：可执行命令（如 npx、python）</summary>
    public string? Command { get; set; }
    /// <summary>stdio 传输：命令参数，JSON 数组序列化</summary>
    public string? ArgsJson { get; set; }
    /// <summary>stdio 传输：环境变量，JSON 对象序列化</summary>
    public string? EnvJson { get; set; }
    /// <summary>SSE 传输：HTTP 端点 URL</summary>
    public string? Url { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; set; }
}
