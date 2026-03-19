using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace MicroClaw.Agent.Tools;

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

        return (tools.AsReadOnly(), [.. connections]);
    }

    private static IClientTransport CreateTransport(McpServerConfig cfg, ILoggerFactory? loggerFactory)
    {
        return cfg.TransportType switch
        {
            McpTransportType.Stdio => new StdioClientTransport(new StdioClientTransportOptions
            {
                Command = cfg.Command
                    ?? throw new InvalidOperationException($"MCP server '{cfg.Name}' requires Command for stdio transport."),
                Arguments = cfg.Args?.ToList(),
                EnvironmentVariables = cfg.Env?.ToDictionary(kv => kv.Key, kv => kv.Value),
                Name = cfg.Name,
            }, loggerFactory!),
            McpTransportType.Sse => new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(cfg.Url
                    ?? throw new InvalidOperationException($"MCP server '{cfg.Name}' requires Url for SSE transport.")),
                Name = cfg.Name,
            }, loggerFactory!),
            _ => throw new NotSupportedException($"Transport type '{cfg.TransportType}' is not supported."),
        };
    }
}
