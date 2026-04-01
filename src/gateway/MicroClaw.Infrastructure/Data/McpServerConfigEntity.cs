namespace MicroClaw.Infrastructure.Data;

public sealed class McpServerConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>传输类型："stdio"、"sse" 或 "http"</summary>
    public string TransportType { get; set; } = "stdio";
    /// <summary>stdio 传输：可执行命令（如 npx、python）</summary>
    public string? Command { get; set; }
    /// <summary>stdio 传输：命令参数，JSON 数组序列化</summary>
    public string? ArgsJson { get; set; }
    /// <summary>stdio 传输：环境变量，JSON 对象序列化</summary>
    public string? EnvJson { get; set; }
    /// <summary>SSE / Http 传输：HTTP 端点 URL</summary>
    public string? Url { get; set; }
    /// <summary>SSE / Http 传输：自定义 HTTP 请求头，JSON 对象序列化</summary>
    public string? HeadersJson { get; set; }
    public bool IsEnabled { get; set; } = true;
    /// <summary>创建时间：相对于 TimeBase.BaseTime 的毫秒偏移。</summary>
    public long CreatedAtMs { get; set; }
    /// <summary>来源：0=手动创建，1=插件注册。</summary>
    public int Source { get; set; } = 0;
    /// <summary>来源插件 ID（仅 Source=Plugin 时有效）。</summary>
    public string? PluginId { get; set; }
    /// <summary>来源插件名称（仅 Source=Plugin 时有效）。</summary>
    public string? PluginName { get; set; }
}
