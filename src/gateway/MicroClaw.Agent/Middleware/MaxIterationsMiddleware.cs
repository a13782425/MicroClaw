using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.Middleware;

/// <summary>
/// 工具调用轮次限制中间件。
/// 提供对 <see cref="FunctionInvokingChatClient.MaximumIterationsPerRequest"/> 的范围校验，
/// 以及一个计数器，用于追踪实际工具调用轮次并在接近或达到上限时记录日志警告。
/// </summary>
public static class MaxIterationsMiddleware
{
    /// <summary>允许的最小迭代轮次。</summary>
    public const int MinIterations = 1;

    /// <summary>允许的最大迭代轮次（硬上限，防止失控循环）。</summary>
    public const int MaxIterations = 50;

    /// <summary>将请求的轮次值夹断到 [<see cref="MinIterations"/>, <see cref="MaxIterations"/>] 范围内。</summary>
    public static int Clamp(int requested) =>
        Math.Clamp(requested, MinIterations, MaxIterations);

    /// <summary>
    /// 创建迭代计数器，用于在 <see cref="FunctionInvoker"/> 中追踪实际工具调用轮次。
    /// 当计数达到上限时记录 Warning 日志。
    /// </summary>
    public static IterationCounter CreateCounter(int maxIterations, ILogger logger) =>
        new(Clamp(maxIterations), logger);
}

/// <summary>线程安全的工具调用轮次计数器。</summary>
public sealed class IterationCounter
{
    private readonly ILogger _logger;
    private int _count;

    /// <summary>本次 Agent 运行允许的最大工具调用轮次。</summary>
    public int MaxIterations { get; }

    /// <summary>已执行的工具调用轮次（1-based）。</summary>
    public int CurrentIteration => _count;

    /// <summary>是否已达到最大轮次上限。</summary>
    public bool IsAtLimit => _count >= MaxIterations;

    internal IterationCounter(int maxIterations, ILogger logger)
    {
        MaxIterations = maxIterations;
        _logger = logger;
    }

    /// <summary>
    /// 原子递增计数，并在接近或达到上限时记录日志。
    /// </summary>
    /// <returns>递增后的当前轮次（1-based）。</returns>
    public int Increment()
    {
        int current = Interlocked.Increment(ref _count);

        if (current >= MaxIterations)
            _logger.LogWarning(
                "Tool iteration limit reached: {Current}/{Max}. No further tool calls will be made.",
                current, MaxIterations);
        else if (current >= MaxIterations - 2)
            _logger.LogDebug(
                "Approaching tool iteration limit: {Current}/{Max}",
                current, MaxIterations);

        return current;
    }

    /// <summary>将计数重置为 0（主要用于测试）。</summary>
    public void Reset() => Interlocked.Exchange(ref _count, 0);
}
