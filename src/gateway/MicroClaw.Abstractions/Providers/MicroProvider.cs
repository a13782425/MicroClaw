using MicroClaw.Abstractions.Streaming;
using MicroClaw.Configuration.Options;
using MicroClaw.Core.Logging;
using Microsoft.Extensions.AI;

namespace MicroClaw.Abstractions.Providers;
/// <summary>
/// 所有模型 Provider 的抽象基类。对外只暴露统一的消息发送/流式接口，
/// 底层 <c>IChatClient</c>/<c>IEmbeddingGenerator</c> 以及 token usage 追踪
/// 均由具体子类内部管理，调用方只需通过 <see cref="ProviderService"/>
/// 按 <see cref="ProviderConfigEntity.Id"/> 拿到实例并传入 <see cref="MicroChatContext"/>。
/// <para>
/// 生命周期：
/// </para>
/// <list type="bullet">
///   <item>由 <see cref="ProviderService"/> 缓存并负责 <see cref="DisposeAsync"/>；</item>
///   <item>当对应 <see cref="ProviderConfigEntity"/> 被修改（哈希变化）时，旧实例会被 dispose 并重新创建；</item>
///   <item>子类应把所有自持有的底层 SDK 客户端在 <see cref="OnDisposeAsync"/> 中释放。</item>
/// </list>
/// </summary>
public abstract class MicroProvider : IAsyncDisposable
{
    private readonly IMicroLogger _logger;
    private int _disposed;
    
    /// <summary>创建 Provider 实例，保存配置快照。</summary>
    /// <param name="configEntity">Provider 配置快照（实例生命周期内不可变）。</param>
    /// <param name="usageTracker">Token usage 追踪器；具体子类通过 <see cref="TrackUsageAsync"/> 等 helper 上报。</param>
    protected MicroProvider(ProviderConfigEntity configEntity, IUsageTracker usageTracker)
    {
        ArgumentNullException.ThrowIfNull(configEntity);
        ArgumentNullException.ThrowIfNull(usageTracker);
        ConfigEntity = configEntity;
        UsageTracker = usageTracker;
        _logger = MicroLogger.Factory.CreateLogger(GetType());
    }
    
    /// <summary>Provider 配置快照，对实例生命周期内不可变。</summary>
    public ProviderConfigEntity ConfigEntity { get; }
    
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
    public virtual Task TrackUsageAsync(MicroChatContext ctx, long inputTokens, long outputTokens = 0L, long cachedInputTokens = 0L)
    {
        throw new NotImplementedException("TrackUsageAsync should be implemented by derived classes if usage tracking is needed.");
    }
    /// <summary>
    /// 使用默认 <see cref="ChatOptions"/>（ModelId/MaxOutputTokens 取自 <see cref="MicroProvider.Config"/>）
    /// 发送一次非流式对话，并在响应可观测到 <see cref="UsageDetails"/> 时自动调用
    /// <see cref="MicroProvider.TrackUsageAsync"/>。
    /// </summary>
    /// <param name="ctx">统一调用上下文（提供 Session/Source/Ct）。</param>
    /// <param name="messages">完整消息序列（包含 system / user / assistant / tool 等）。</param>
    /// <param name="options">
    ///     可选的 <see cref="ChatOptions"/> 覆盖。传入 <c>null</c> 时使用
    ///     传入非 null 时按原值下发，
    ///     基类不再强制覆盖 ModelId / MaxOutputTokens。
    /// </param>
    public virtual Task<ChatResponse> ChatAsync(MicroChatContext ctx, IEnumerable<ChatMessage> messages, ChatOptions? options = null)
    {
        throw new NotImplementedException("ChatAsync should be implemented by derived classes.");
    }
    
    /// <summary>
    /// 完整的 Agent 循环：模型产出 token 和 thinking → 命中工具调用 → 执行工具 → 写入结果 →
    /// 继续循环直到模型给出最终回复。
    /// <para>
    /// 产出的 <see cref="StreamItem"/> 序列包含：<see cref="TokenItem"/>、<see cref="ThinkingItem"/>、
    /// <see cref="DataContentItem"/>（图片/音频等）、<see cref="ToolCallItem"/>、<see cref="ToolResultItem"/>。
    /// </para>
    /// </summary>
    /// <param name="ctx">统一调用上下文；<see cref="MicroChatContext.Ct"/> 与 <paramref name="ct"/> 之间以参数为准（通常两者相同）。</param>
    /// <param name="messages">初始消息序列。</param>
    /// <param name="tools">工具列表；为空表示禁止 function calling。</param>
    /// <param name="options">
    ///     可选的 <see cref="ChatOptions"/> 覆盖；传入 <c>null</c> 时由
    /// </param>
    /// <param name="internalToolNames">
    ///     可选的"内部工具"名单。命中时对应的 <see cref="ToolCallItem"/> / <see cref="ToolResultItem"/>
    ///     的 <see cref="StreamItem.Visibility"/> 会被设为
    /// </param>
    public virtual IAsyncEnumerable<StreamItem> AgentStreamAsync(MicroChatContext ctx, IEnumerable<ChatMessage> messages, IReadOnlyList<AITool> tools, ChatOptions? options = null, IReadOnlySet<string>? internalToolNames = null)
    {
        throw new NotImplementedException("AgentStreamAsync should be implemented by derived classes.");
    }
    /// <summary>
    /// 批量生成嵌入向量，同时根据响应的 <see cref="UsageDetails"/> 自动调用
    /// <see cref="MicroProvider.TrackUsageAsync"/> 记录用量。
    /// </summary>
    /// <param name="ctx">统一调用上下文（提供 Session/Source/Ct）。</param>
    /// <param name="inputs">待向量化的文本列表。空列表时直接返回空结果，不触发网络请求。</param>
    public virtual Task<IReadOnlyList<Embedding<float>>> EmbedBatchAsync(MicroChatContext ctx, IReadOnlyList<string> inputs)
    {
        throw new NotImplementedException("EmbedBatchAsync should be implemented by derived classes if embedding is needed.");
    }
    
    
    // /// <summary>
    // /// 将一次 chat 调用的 usage 数据按 Provider 价格计算后写入 <see cref="IUsageTracker"/>。
    // /// 取消路径（<see cref="OperationCanceledException"/>）下由调用方捕获并决定是否上报，
    // /// 此处对内部异常只记录 Warning，避免 usage 追踪失败影响主路径。
    // /// </summary>
    // /// <param name="ctx">Provider 调用上下文。</param>
    // /// <param name="inputTokens">本次调用的输入 token 总数（含 cache）。</param>
    // /// <param name="outputTokens">本次调用的输出 token 总数。</param>
    // /// <param name="cachedInputTokens">命中缓存的输入 token（来自底层 SDK）。</param>
    // protected async Task TrackChatUsageAsync(MicroChatContext ctx, long inputTokens, long outputTokens, long cachedInputTokens = 0L)
    // {
    //     ArgumentNullException.ThrowIfNull(ctx);
    //     if (inputTokens <= 0 && outputTokens <= 0) return;
    //     
    //     long nonCachedInput = Math.Max(0L, inputTokens - cachedInputTokens);
    //     decimal inputCost = nonCachedInput > 0 && Config.Capabilities.InputPricePerMToken.HasValue ? nonCachedInput * Config.Capabilities.InputPricePerMToken.Value / 1_000_000m : 0m;
    //     decimal outputCost = outputTokens > 0 && Config.Capabilities.OutputPricePerMToken.HasValue ? outputTokens * Config.Capabilities.OutputPricePerMToken.Value / 1_000_000m : 0m;
    //     decimal cacheInputCost = cachedInputTokens > 0 ? cachedInputTokens * (Config.Capabilities.CacheInputPricePerMToken ?? Config.Capabilities.InputPricePerMToken ?? 0m) / 1_000_000m : 0m;
    //     
    //     try
    //     {
    //         await UsageTracker.TrackAsync(ctx.Session.Id, Config.Id, Config.DisplayName, ctx.Source, inputTokens, outputTokens, cachedInputTokens, inputCost, outputCost, cacheInputCost, cacheOutputCostUsd: 0m,
    //             // TODO: 在 MicroChatContext 增加 AgentId / MonthlyBudgetUsd 字段后透传，
    //             //       当前 Agent 预算告警暂时走不到，等 AgentRunner 迁移完整补回。
    //             agentId: null, monthlyBudgetUsd: null, ct: CancellationToken.None);
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogWarning(ex, "Usage tracking failed for provider {ProviderId} session {SessionId}", Config.Id, ctx.Session.Id);
    //     }
    // }
    //
    // /// <summary>
    // /// 将一次 embedding 调用的 usage 写入 <see cref="IUsageTracker"/>。
    // /// 价格按 <see cref="ProviderCapabilities.InputPricePerMToken"/> 估算（embedding 无 output token）。
    // /// </summary>
    // protected async Task TrackEmbeddingUsageAsync(MicroChatContext ctx, long inputTokens)
    // {
    //     ArgumentNullException.ThrowIfNull(ctx);
    //     if (inputTokens <= 0) return;
    //     
    //     decimal inputCost = Config.Capabilities.InputPricePerMToken.HasValue ? inputTokens * Config.Capabilities.InputPricePerMToken.Value / 1_000_000m : 0m;
    //     
    //     try
    //     {
    //         await UsageTracker.TrackAsync(ctx.Session.Id, Config.Id, Config.DisplayName, ctx.Source, inputTokens, outputTokens: 0L, cachedInputTokens: 0L, inputCost, outputCostUsd: 0m, cacheInputCostUsd: 0m, cacheOutputCostUsd: 0m, agentId: null, monthlyBudgetUsd: null, ct: CancellationToken.None);
    //     }
    //     catch (Exception ex)
    //     {
    //         _logger.LogWarning(ex, "Embedding usage tracking failed for provider {ProviderId} session {SessionId}", Config.Id, ctx.Session.Id);
    //     }
    // }
    
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
            _logger.LogWarning(ex, "MicroProvider {ProviderId} OnDisposeAsync threw; continuing teardown.", ConfigEntity.Id);
        }
        GC.SuppressFinalize(this);
    }
}