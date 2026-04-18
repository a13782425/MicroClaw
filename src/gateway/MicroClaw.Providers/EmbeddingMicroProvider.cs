using MicroClaw.Abstractions;
using MicroClaw.Infrastructure.Data;
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

    /// <summary>创建 Embedding 类 Provider。</summary>
    protected EmbeddingMicroProvider(ProviderConfig config, IUsageTracker usageTracker)
        : base(config, usageTracker)
    {
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
    /// <see cref="MicroProvider.TrackEmbeddingUsageAsync"/> 记录用量。
    /// </summary>
    /// <param name="ctx">统一调用上下文（提供 Session/Source/Ct）。</param>
    /// <param name="inputs">待向量化的文本列表。空列表时直接返回空结果，不触发网络请求。</param>
    public virtual async Task<IReadOnlyList<Embedding<float>>> EmbedBatchAsync(
        MicroChatContext ctx,
        IReadOnlyList<string> inputs)
    {
        CancellationToken ct = ValidateContext(ctx);
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0) return Array.Empty<Embedding<float>>();

        GeneratedEmbeddings<Embedding<float>> result =
            await Generator.GenerateAsync(inputs, options: null, ct);

        if (result.Usage is { InputTokenCount: { } inputTokens } && inputTokens > 0)
            await TrackEmbeddingUsageAsync(ctx, inputTokens);

        // GeneratedEmbeddings<T> 实现了 IReadOnlyList<T>，直接返回副本以隔离调用方。
        var list = new List<Embedding<float>>(result.Count);
        list.AddRange(result);
        return list.AsReadOnly();
    }

    /// <inheritdoc />
    protected override async ValueTask OnDisposeAsync()
    {
        IEmbeddingGenerator<string, Embedding<float>>? g =
            Interlocked.Exchange(ref _generator, null);
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
