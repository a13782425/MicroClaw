namespace MicroClaw.Safety;

/// <summary>
/// 工具拦截结果。表示拦截器对当前工具调用的处置决定。
/// </summary>
public sealed record ToolInterceptResult
{
    /// <summary>是否允许执行。为 <c>false</c> 时工具调用将被阻止。</summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// 阻止原因（当 <see cref="IsAllowed"/> 为 <c>false</c> 时填充）。
    /// 允许时为 <c>null</c>。
    /// </summary>
    public string? BlockReason { get; init; }

    private ToolInterceptResult() { }

    /// <summary>返回「允许执行」结果。</summary>
    public static ToolInterceptResult Allow() => new() { IsAllowed = true };

    /// <summary>返回「阻止执行」结果，并附带说明原因。</summary>
    public static ToolInterceptResult Block(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("阻止原因不能为空", nameof(reason));
        return new() { IsAllowed = false, BlockReason = reason };
    }
}
