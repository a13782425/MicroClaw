namespace MicroClaw.Tools;

/// <summary>MCP Server 连接配置，支持 stdio（本地进程）、SSE（HTTP 远程旧协议）和 Http（Streamable HTTP 新协议）三种传输方式。</summary>
/// <remarks>
/// stdio 示例：npx -y @modelcontextprotocol/server-filesystem /workspace
/// SSE 示例：http://my-python-service:8080/sse
/// Http 示例：https://open.bigmodel.cn/api/mcp/web_search_prime/mcp（带 Authorization header）
/// </remarks>
public sealed record McpServerConfig(
    string Name,
    McpTransportType TransportType,
    // stdio transport
    string? Command = null,
    IReadOnlyList<string>? Args = null,
    IDictionary<string, string?>? Env = null,
    // SSE / Http transport
    string? Url = null,
    IDictionary<string, string>? Headers = null,
    // 全局管理元数据
    string Id = "",
    bool IsEnabled = true,
    DateTimeOffset CreatedAtUtc = default,
    // 来源信息
    McpServerSource Source = McpServerSource.Manual,
    string? PluginId = null,
    string? PluginName = null);

public enum McpTransportType
{
    Stdio,
    Sse,
    Http,
}

/// <summary>MCP Server 配置来源。</summary>
public enum McpServerSource
{
    /// <summary>通过 UI 手动创建。</summary>
    Manual = 0,
    /// <summary>由插件自动注册（来自插件目录中的 .mcp.json）。</summary>
    Plugin = 1,
}
