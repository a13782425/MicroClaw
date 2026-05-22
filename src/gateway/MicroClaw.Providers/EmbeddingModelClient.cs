using MicroClaw.Common;
using MicroClaw.Configuration;
using MicroClaw.Core.Logging;
using MicroClaw.Providers.Mapping;
using Microsoft.Extensions.AI;

namespace MicroClaw.Providers;

/// <summary>
/// Embedding 类 Provider 的运行时基类。子类只需实现 <see cref="BuildGenerator"/>。
/// 基类负责懒加载底层生成器、批量调用以及 usage 追踪。
/// </summary>
public abstract class EmbeddingModelClient : ModelProviderObject
{
    private readonly object _generatorLock = new();
    private IEmbeddingGenerator<string, Embedding<float>>? _generator;

    protected EmbeddingModelClient(ProviderEntityConfig config, IUsageTracker usageTracker) : base(config, usageTracker)
    {
        ModelKind kind = ProviderConfigOps.ParseModelKind(config.ModelKind);
        if (kind != ModelKind.Embedding)
            throw new InvalidOperationException($"EmbeddingModelClient requires ModelKind.Embedding (got {kind}).");
    }

    /// <summary>懒加载的底层 <see cref="IEmbeddingGenerator{String,Embedding}"/>。</summary>
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

    /// <summary>构造底层嵌入生成器；由具体 Provider 子类实现。</summary>
    protected abstract IEmbeddingGenerator<string, Embedding<float>> BuildGenerator();

    /// <inheritdoc />
    public override async Task<IReadOnlyList<Embedding<float>>> EmbedBatchAsync(MicroChatContext ctx, IReadOnlyList<string> inputs)
    {
        CancellationToken ct = ValidateContext(ctx);
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0) return Array.Empty<Embedding<float>>();

        GeneratedEmbeddings<Embedding<float>> result = await Generator.GenerateAsync(inputs, options: null, ct);

        if (result.Usage is { InputTokenCount: { } inputTokens } && inputTokens > 0)
            await TrackUsageAsync(ctx, inputTokens);

        var list = new List<Embedding<float>>(result.Count);
        list.AddRange(result);
        return list.AsReadOnly();
    }

    /// <inheritdoc />
    public override async Task TrackUsageAsync(MicroChatContext ctx, long inputTokens, long outputTokens = 0L, long cachedInputTokens = 0L)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (inputTokens <= 0) return;

        ModelPricing pricing = Pricing;
        decimal inputCost = pricing.InputPerMillionTokens.HasValue
            ? inputTokens * pricing.InputPerMillionTokens.Value / 1_000_000m : 0m;

        try
        {
            await UsageTracker.TrackAsync(
                ctx.Session.Id, Id, DisplayName, ctx.Source,
                inputTokens, outputTokens: 0L, cachedInputTokens: 0L,
                inputCost, outputCostUsd: 0m, cacheInputCostUsd: 0m, cacheOutputCostUsd: 0m,
                agentId: null, monthlyBudgetUsd: null, ct: ctx.Ct);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Embedding usage tracking failed for provider {ProviderId} session {SessionId}", Id, ctx.Session.Id);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposedAsync(CancellationToken cancellationToken = default)
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
        await base.OnDisposedAsync(cancellationToken);
    }
}
