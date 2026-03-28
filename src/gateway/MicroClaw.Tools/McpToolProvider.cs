using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace MicroClaw.Tools;

/// <summary>
/// MCP 工具提供者 — 对应单个 MCP Server，通过 <see cref="ToolRegistry"/> 连接并加载工具列表。
/// 不通过 DI 注册（MCP Server 是运行时可配置的），由 ToolCollector 动态创建实例。
/// </summary>
public sealed class McpToolProvider(McpServerConfig config, ILoggerFactory loggerFactory) : IToolProvider
{
    public ToolCategory Category => ToolCategory.Mcp;
    public string GroupId => config.Id;
    public string DisplayName => config.Name;

    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() => [];

    public async Task<ToolProviderResult> CreateToolsAsync(ToolCreationContext context, CancellationToken ct = default)
    {
        var (tools, connections) = await ToolRegistry.LoadToolsAsync([config], loggerFactory, ct);
        return new ToolProviderResult(tools, connections);
    }
}
