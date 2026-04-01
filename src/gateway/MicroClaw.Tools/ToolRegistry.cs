using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace MicroClaw.Tools;

/// <summary>
/// MCP 工具注册表：按需创建 MCP Client 连接，获取工具列表。
/// 兼容 npm/pip 上的所有现成 MCP Server（Node.js、Python、远程 HTTP）。
/// </summary>
public static class ToolRegistry
{
    /// <summary>
    /// 连接所有配置的 MCP Server，返回合并的工具列表和对应连接（调用方负责释放）。
    /// </summary>
    public static async Task<(IReadOnlyList<McpClientTool> Tools, IAsyncDisposable[] Connections)> LoadToolsAsync(
        IReadOnlyList<McpServerConfig> configs,
        ILoggerFactory? loggerFactory = null,
        CancellationToken ct = default)
    {
        if (configs.Count == 0)
            return ([], []);

        var tools = new List<McpClientTool>();
        var connections = new List<IAsyncDisposable>();

        try
        {
            foreach (McpServerConfig config in configs)
            {
                IClientTransport transport = CreateTransport(config, loggerFactory);
                // McpClient.CreateAsync 是 v1.1.0 的工厂方法（取代了旧的 McpClientFactory.CreateAsync）
                McpClient client = await McpClient.CreateAsync(
                    transport, clientOptions: null, loggerFactory: loggerFactory, cancellationToken: ct);
                connections.Add(client);

                IList<McpClientTool> mcpTools = await client.ListToolsAsync(cancellationToken: ct);
                tools.AddRange(mcpTools);
            }
        }
        catch
        {
            // 连接中途失败时，释放已建立的连接，避免资源泄漏
            foreach (IAsyncDisposable conn in connections)
            {
                try { await conn.DisposeAsync(); } catch { /* 忽略 dispose 异常，避免掩盖原始异常 */ }
            }
            throw;
        }

        return (tools.AsReadOnly(), [.. connections]);
    }

    private static IClientTransport CreateTransport(McpServerConfig cfg, ILoggerFactory? loggerFactory)
    {
        McpServerConfig resolved = McpConfigurationResolver.ResolveEnvironmentVariables(cfg);

        return resolved.TransportType switch
        {
            McpTransportType.Stdio => new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = resolved.Command
                    ?? throw new InvalidOperationException($"MCP server '{resolved.Name}' requires Command for stdio transport."),
                Arguments = resolved.Args?.ToList(),
                EnvironmentVariables = resolved.Env?.ToDictionary(kv => kv.Key, kv => kv.Value),
                Name = resolved.Name,
            }, loggerFactory!),
            McpTransportType.Sse or McpTransportType.Http => new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(resolved.Url
                    ?? throw new InvalidOperationException($"MCP server '{resolved.Name}' requires Url for {resolved.TransportType} transport.")),
                Name = resolved.Name,
                TransportMode = resolved.TransportType == McpTransportType.Http
                    ? HttpTransportMode.StreamableHttp
                    : HttpTransportMode.Sse,
                AdditionalHeaders = resolved.Headers,
            }, loggerFactory!),
            _ => throw new NotSupportedException($"Transport type '{resolved.TransportType}' is not supported."),
        };
    }
}