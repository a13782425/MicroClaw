using Microsoft.Extensions.AI;

namespace MicroClaw.Tools;

/// <summary>
/// 工具创建结果 — <see cref="IToolProvider.CreateToolsAsync"/> 的返回值。
/// 包含创建的工具列表和可选的需要释放的资源（如 MCP 连接）。
/// </summary>
public sealed record ToolProviderResult(
    /// <summary>创建的工具实例列表。</summary>
    IReadOnlyList<AITool> Tools,
    /// <summary>需要在使用完毕后释放的资源（仅 MCP Provider 会返回连接对象）。</summary>
    IReadOnlyList<IAsyncDisposable>? Disposables = null)
{
    /// <summary>无工具的空结果。</summary>
    public static readonly ToolProviderResult Empty = new([]);
}
