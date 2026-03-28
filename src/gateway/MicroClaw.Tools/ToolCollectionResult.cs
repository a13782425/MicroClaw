using Microsoft.Extensions.AI;

namespace MicroClaw.Tools;

/// <summary>
/// 工具收集结果 — 包含最终的工具列表和需要在使用完毕后释放的资源（如 MCP 连接）。
/// 使用 <c>await using</c> 模式确保资源正确释放。
/// </summary>
public sealed class ToolCollectionResult : IAsyncDisposable
{
    private readonly List<AITool> _tools = [];
    private readonly List<IAsyncDisposable> _disposables = [];

    /// <summary>所有已收集的工具列表。</summary>
    public IReadOnlyList<AITool> AllTools => _tools;

    public void AddTools(IEnumerable<AITool> tools) => _tools.AddRange(tools);

    public void TrackDisposables(IReadOnlyList<IAsyncDisposable> disposables)
        => _disposables.AddRange(disposables);

    public async ValueTask DisposeAsync()
    {
        foreach (IAsyncDisposable disposable in _disposables)
        {
            try { await disposable.DisposeAsync(); } catch { }
        }
        _disposables.Clear();
    }
}
