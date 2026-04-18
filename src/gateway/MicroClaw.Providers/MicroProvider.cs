using MicroClaw.Abstractions;
using MicroClaw.Core.Logging;
using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Providers;

/// <summary>
/// 所有模型 Provider 的抽象基类。对外只暴露统一的消息发送/流式接口，
/// 底层 <c>IChatClient</c>/<c>IEmbeddingGenerator</c> 以及 token usage 追踪
/// 均由具体子类内部管理，调用方只需通过 <see cref="ProviderService"/>
/// 按 <see cref="ProviderConfig.Id"/> 拿到实例并传入 <see cref="MicroChatContext"/>。
/// <para>
/// 生命周期：
/// </para>
/// <list type="bullet">
///   <item>由 <see cref="ProviderService"/> 缓存并负责 <see cref="DisposeAsync"/>；</item>
///   <item>当对应 <see cref="ProviderConfig"/> 被修改（哈希变化）时，旧实例会被 dispose 并重新创建；</item>
///   <item>子类应把所有自持有的底层 SDK 客户端在 <see cref="OnDisposeAsync"/> 中释放。</item>
/// </list>
/// </summary>
public abstract class MicroProvider : IAsyncDisposable
{
    private readonly IMicroLogger _logger;
    private int _disposed;

    /// <summary>创建 Provider 实例，保存配置快照。</summary>
    /// <param name="config">Provider 配置快照（实例生命周期内不可变）。</param>
    /// <param name="usageTracker">Token usage 追踪器；具体子类通过 <see cref="TrackChatUsageAsync"/> 等 helper 上报。</param>
    protected MicroProvider(ProviderConfig config, IUsageTracker usageTracker)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(usageTracker);
        Config = config;
        UsageTracker = usageTracker;
        _logger = MicroLogger.Factory.CreateLogger(GetType());
    }

    /// <summary>Provider 配置快照，对实例生命周期内不可变。</summary>
    public ProviderConfig Config { get; }

    /// <summary>Token usage 追踪器。子类在调用底层 SDK 后调用对应 helper 上报。</summary>
    protected IUsageTracker UsageTracker { get; }

    /// <summary>日志器，分类名取自运行时类型。</summary>
    protected IMicroLogger Logger => _logger;

    /// <summary>当前实例是否已被 dispose。</summary>
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <summary>
    /// 将一次 chat 调用的 usage 数据按 Provider 价格计算后写入 <see cref="IUsageTracker"/>。
    /// 取消路径（<see cref="OperationCanceledException"/>）下由调用方捕获并决定是否上报，
    /// 此处对内部异常只记录 Warning，避免 usage 追踪失败影响主路径。
    /// </summary>
    /// <param name="ctx">Provider 调用上下文。</param>
    /// <param name="inputTokens">本次调用的输入 token 总数（含 cache）。</param>
    /// <param name="outputTokens">本次调用的输出 token 总数。</param>
    /// <param name="cachedInputTokens">命中缓存的输入 token（来自底层 SDK）。</param>
    protected async Task TrackChatUsageAsync(
        MicroChatContext ctx,
        long inputTokens,
        long outputTokens,
        long cachedInputTokens = 0L)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (inputTokens <= 0 && outputTokens <= 0) return;

        long nonCachedInput = Math.Max(0L, inputTokens - cachedInputTokens);
        decimal inputCost = nonCachedInput > 0 && Config.Capabilities.InputPricePerMToken.HasValue
            ? nonCachedInput * Config.Capabilities.InputPricePerMToken.Value / 1_000_000m
            : 0m;
        decimal outputCost = outputTokens > 0 && Config.Capabilities.OutputPricePerMToken.HasValue
            ? outputTokens * Config.Capabilities.OutputPricePerMToken.Value / 1_000_000m
            : 0m;
        decimal cacheInputCost = cachedInputTokens > 0
            ? cachedInputTokens *
              (Config.Capabilities.CacheInputPricePerMToken ?? Config.Capabilities.InputPricePerMToken ?? 0m)
              / 1_000_000m
            : 0m;

        try
        {
            await UsageTracker.TrackAsync(
                ctx.Session.Id,
                Config.Id,
                Config.DisplayName,
                ctx.Source,
                inputTokens,
                outputTokens,
                cachedInputTokens,
                inputCost,
                outputCost,
                cacheInputCost,
                cacheOutputCostUsd: 0m,
                // TODO: 在 MicroChatContext 增加 AgentId / MonthlyBudgetUsd 字段后透传，
                //       当前 Agent 预算告警暂时走不到，等 AgentRunner 迁移完整补回。
                agentId: null,
                monthlyBudgetUsd: null,
                ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Usage tracking failed for provider {ProviderId} session {SessionId}",
                Config.Id, ctx.Session.Id);
        }
    }

    /// <summary>
    /// 将一次 embedding 调用的 usage 写入 <see cref="IUsageTracker"/>。
    /// 价格按 <see cref="ProviderCapabilities.InputPricePerMToken"/> 估算（embedding 无 output token）。
    /// </summary>
    protected async Task TrackEmbeddingUsageAsync(MicroChatContext ctx, long inputTokens)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (inputTokens <= 0) return;

        decimal inputCost = Config.Capabilities.InputPricePerMToken.HasValue
            ? inputTokens * Config.Capabilities.InputPricePerMToken.Value / 1_000_000m
            : 0m;

        try
        {
            await UsageTracker.TrackAsync(
                ctx.Session.Id,
                Config.Id,
                Config.DisplayName,
                ctx.Source,
                inputTokens,
                outputTokens: 0L,
                cachedInputTokens: 0L,
                inputCost,
                outputCostUsd: 0m,
                cacheInputCostUsd: 0m,
                cacheOutputCostUsd: 0m,
                agentId: null,
                monthlyBudgetUsd: null,
                ct: CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Embedding usage tracking failed for provider {ProviderId} session {SessionId}",
                Config.Id, ctx.Session.Id);
        }
    }

    /// <summary>校验上下文字段（Session/Source 必填）并返回 Ct。</summary>
    protected static CancellationToken ValidateContext(MicroChatContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Session is null)
            throw new ArgumentException("MicroChatContext.Session is required.", nameof(ctx));
        if (string.IsNullOrWhiteSpace(ctx.Source))
            throw new ArgumentException("MicroChatContext.Source is required.", nameof(ctx));
        return ctx.Ct;
    }

    /// <summary>释放底层 SDK 客户端；子类实现。基类保证只触发一次。</summary>
    protected virtual ValueTask OnDisposeAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            await OnDisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MicroProvider {ProviderId} OnDisposeAsync threw; continuing teardown.",
                Config.Id);
        }
        GC.SuppressFinalize(this);
    }
}
