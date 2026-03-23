namespace MicroClaw.Tools;

/// <summary>MCP Server 连接配置，支持 stdio（本地进程）和 SSE（HTTP 远程）两种传输方式。</summary>
/// <remarks>
/// stdio 示例：npx -y @modelcontextprotocol/server-filesystem /workspace
/// SSE 示例：http://my-python-service:8080/sse
/// </remarks>
public sealed record McpServerConfig(
    string Name,
    McpTransportType TransportType,
    // stdio transport
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    IDictionary<string, string?>? Env = null,
    // SSE transport
    string? Url = null,
    // 全局管理元数据
    string Id = "",
    bool IsEnabled = true,
    DateTimeOffset CreatedAtUtc = default);

public enum McpTransportType
{
    Stdio,
    Sse,
}
