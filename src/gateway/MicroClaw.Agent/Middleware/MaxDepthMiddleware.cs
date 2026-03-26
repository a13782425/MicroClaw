namespace MicroClaw.Agent.Middleware;

/// <summary>
/// 子代理递归深度限制中间件。
/// 使用 <see cref="AsyncLocal{T}"/> 在当前异步执行上下文中追踪子代理调用深度，
/// 防止子代理互相调用导致的无限递归或过深的调用链。
/// </summary>
public static class MaxDepthMiddleware
{
    private static readonly AsyncLocal<int> _depth = new();

    /// <summary>默认最大递归深度。</summary>
    public const int DefaultMaxDepth = 5;

    /// <summary>获取当前异步上下文中的子代理调用深度（0 表示顶层）。</summary>
    public static int CurrentDepth => _depth.Value;

    /// <summary>
    /// 将当前深度递增 1 后执行 <paramref name="operation"/>，完成后自动恢复深度。
    /// 若递增后的深度超过 <paramref name="maxDepth"/>，则在执行前抛出 <see cref="MaxDepthExceededException"/>。
    /// </summary>
    /// <param name="operation">要执行的异步操作。</param>
    /// <param name="maxDepth">允许的最大递归深度（包含）。</param>
    /// <returns>操作的返回值。</returns>
    /// <exception cref="MaxDepthExceededException">深度超过 <paramref name="maxDepth"/> 时抛出。</exception>
    public static async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        int maxDepth = DefaultMaxDepth)
    {
        int depth = _depth.Value + 1;

        if (depth > maxDepth)
            throw new MaxDepthExceededException(depth, maxDepth);

        _depth.Value = depth;
        try
        {
            return await operation();
        }
        finally
        {
            _depth.Value = depth - 1;
        }
    }

    /// <summary>
    /// 检查当前深度是否允许继续深入一层。
    /// 若已达到或超过 <paramref name="maxDepth"/>，则抛出 <see cref="MaxDepthExceededException"/>。
    /// </summary>
    /// <param name="maxDepth">允许的最大递归深度（包含）。</param>
    /// <exception cref="MaxDepthExceededException">深度已达上限时抛出。</exception>
    public static void CheckDepth(int maxDepth = DefaultMaxDepth)
    {
        if (_depth.Value >= maxDepth)
            throw new MaxDepthExceededException(_depth.Value + 1, maxDepth);
    }
}

/// <summary>子代理递归调用深度超过允许上限时抛出的异常。</summary>
public sealed class MaxDepthExceededException(int currentDepth, int maxDepth)
    : InvalidOperationException(
        $"Sub-agent recursion depth {currentDepth} exceeds maximum allowed depth {maxDepth}.")
{
    /// <summary>触发异常时的当前深度（已超过上限的值）。</summary>
    public int CurrentDepth { get; } = currentDepth;

    /// <summary>配置的最大允许深度。</summary>
    public int MaxDepth { get; } = maxDepth;
}
