using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Providers;
using MicroClaw.Configuration.Options;
using MicroClaw.Core.Logging;
using Microsoft.Extensions.AI;

namespace MicroClaw.Providers;
/// <summary>
/// Embedding 类 Provider 的抽象基类。具体子类只需实现 <see cref="BuildGenerator"/>，
/// 提供对应 SDK 封装的 <see cref="IEmbeddingGenerator{String,Embedding}"/>；
/// 基类负责懒加载底层生成器、批量调用与 usage 追踪。
/// </summary>
public abstract class EmbeddingMicroProvider : MicroProvider
{
    private readonly object _generatorLock = new();
    private IEmbeddingGenerator<string, Embedding<float>>? _generator;
    
    protected ProviderConfig Config { get; init; }
    
    /// <summary>创建 Embedding 类 Provider。</summary>
    protected EmbeddingMicroProvider(ProviderConfigEntity configEntity, IUsageTracker usageTracker) : base(configEntity, usageTracker)
    {
        Config = configEntity.ToConfig();
    }
    
    /// <summary>懒加载的底层 <see cref="IEmbeddingGenerator{String,Embedding}"/>；同一实例内复用。</summary>
    protected IEmbeddingGenerator<string, Embedding<float>> Generator
    {
        get
        {
            if (_generator is not null) return _generator;
            lock (_generatorLock)
            {
                _generator ??= BuildGenerator();
                return _generator;
            }
        }
    }
    
    /// <summary>构造底层 Embedding 生成器的工厂方法，由具体 Provider 子类实现。</summary>
    protected abstract IEmbeddingGenerator<string, Embedding<float>> BuildGenerator();
    
    /// <summary>
    /// 批量生成嵌入向量，同时根据响应的 <see cref="UsageDetails"/> 自动调用
    /// <see cref="MicroProvider.TrackUsageAsync"/> 记录用量。
    /// </summary>
    /// <param name="ctx">统一调用上下文（提供 Session/Source/Ct）。</param>
    /// <param name="inputs">待向量化的文本列表。空列表时直接返回空结果，不触发网络请求。</param>
    public override async Task<IReadOnlyList<Embedding<float>>> EmbedBatchAsync(MicroChatContext ctx, IReadOnlyList<string> inputs)
    {
        CancellationToken ct = ValidateContext(ctx);
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0) return Array.Empty<Embedding<float>>();
        
        GeneratedEmbeddings<Embedding<float>> result = await Generator.GenerateAsync(inputs, options: null, ct);
        
        if (result.Usage is { InputTokenCount: { } inputTokens } && inputTokens > 0)
            await TrackUsageAsync(ctx, inputTokens);
        
        // GeneratedEmbeddings<T> 实现了 IReadOnlyList<T>，直接返回副本以隔离调用方。
        var list = new List<Embedding<float>>(result.Count);
        list.AddRange(result);
        return list.AsReadOnly();
    }
    public override async Task TrackUsageAsync(MicroChatContext ctx, long inputTokens, long outputTokens = 0L, long cachedInputTokens = 0L)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (inputTokens <= 0) return;
        
        decimal inputCost = Config.Capabilities.InputPricePerMToken.HasValue ? inputTokens * Config.Capabilities.InputPricePerMToken.Value / 1_000_000m : 0m;
        
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
                ctx.Ct);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Embedding usage tracking failed for provider {ProviderId} session {SessionId}", Config.Id, ctx.Session.Id);
        }
    }
    
    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        IEmbeddingGenerator<string, Embedding<float>>? g = Interlocked.Exchange(ref _generator, null);
        switch (g)
        {
            case IAsyncDisposable ad:
                await ad.DisposeAsync();
                break;
            case IDisposable d:
                d.Dispose();
                break;
        }
    }
}