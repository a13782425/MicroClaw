using MicroClaw.Common;
using MicroClaw.Configuration;
using MicroClaw.Core;
using MicroClaw.Core.Logging;
using MicroClaw.Database;

using MicroClaw.Utils;
using Microsoft.Extensions.AI;
using System.Security.Cryptography;
using System.Text;

namespace MicroClaw.Providers;

/// <summary>
/// 模型提供方运行时基类。一个 <see cref="ModelProviderObject"/> 实例对应一份 <see cref="ProviderEntityConfig"/>，
/// 由 <see cref="ModelProviderService"/> 在启动阶段一次性创建并持有；不向引擎容器注册（避免在
/// <see cref="MicroService.StartAsync"/> 内部触发引擎执行门重入）。
/// <para>
/// 落地配置只有一份：<see cref="Config"/>（YAML DTO）。所有强类型视图（枚举、Flags、Pricing 等）
/// 通过 <see cref="ProviderUtils"/> 在访问时解析；配置变更通过 <see cref="ModelProviderService"/>
/// 落盘 YAML 后替换 <see cref="ModelProviderObject"/> 实例。
/// </para>
/// </summary>
public abstract class ModelProviderObject : MicroObject
{
    private const decimal PerMillionFactor = 1 / 1_000_000m;

    protected ModelProviderObject(ProviderEntityConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.Id))
            throw new InvalidOperationException("ProviderEntityConfig.Id is required.");

        Config = config;
    }

    /// <summary>底层 YAML 配置（仅子类可见，不向外公开）。</summary>
    public ProviderEntityConfig Config { get; }

    /// <summary>Provider 的唯一标识。</summary>
    public string Id => Config.Id;

    /// <summary>面向用户的展示名称（缺省回落到 Id）。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Config.DisplayName) ? Config.Id : Config.DisplayName;

    /// <summary>是否启用。</summary>
    public bool IsEnabled => Config.IsEnabled;

    /// <summary>是否被标记为同 ModelKind 下的默认。</summary>
    public bool IsDefault => Config.IsDefault;

    // ── Strongly-typed views over Config (parsed on demand) ──────────────

    /// <summary>API 协议族。</summary>
    public ModelProviderApiKind ApiKind => ProviderUtils.ParseApiKind(Config.ApiKind);

    /// <summary>模型用途（chat / embedding）。</summary>
    public ModelKind Kind => ProviderUtils.ParseModelKind(Config.ModelKind);

    /// <summary>解析 <c>${ENV}</c> 后的模型名称。</summary>
    public string ModelName => MicroClawUtils.ResolveEnv(Config.ModelName) ?? string.Empty;

    /// <summary>解析 <c>${ENV}</c> 后的 API Key。</summary>
    public string ApiKey => MicroClawUtils.ResolveEnv(Config.ApiKey) ?? string.Empty;

    /// <summary>解析后并 trim 末尾斜杠的 BaseUrl；空时返回 null。</summary>
    public string? BaseUrl => ProviderUtils.NormalizeBaseUrl(MicroClawUtils.ResolveEnv(Config.BaseUrl));

    /// <summary>输入模态 Flags（含 <see cref="ModelInputModality.ToolCall"/> 工具调用能力）。</summary>
    public ModelInputModality InputModalities => ProviderUtils.ParseInputModalities(Config.InputModalities, ModelInputModality.Text);

    /// <summary>输出模态 Flags。</summary>
    public ModelOutputModality OutputModalities => ProviderUtils.ParseOutputModalities(Config.OutputModalities, ModelOutputModality.Text);

    /// <summary>单次输出的最大 Token 数（无效值回退 8192）。</summary>
    public int MaxOutputTokens => Config.MaxOutputTokens > 0 ? Config.MaxOutputTokens : 8192;

    /// <summary>校验 <see cref="MicroChatContext"/> 的最小必需字段，并返回取消令牌。</summary>
    protected static CancellationToken ValidateContext(MicroChatContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (ctx.Session is null)
            throw new InvalidOperationException("MicroChatContext.Session is required.");
        if (string.IsNullOrEmpty(ctx.Source))
            throw new InvalidOperationException("MicroChatContext.Source is required.");
        return ctx.Ct;
    }

    /// <summary>非流式对话。仅 Chat 类 Provider 需要实现。</summary>
    public virtual Task<ChatResponse> ChatAsync(MicroChatContext ctx, IEnumerable<ChatMessage> messages, ChatOptions? options = null) =>
        throw new NotImplementedException("ChatAsync is only supported by ChatModelClient.");

    /// <summary>流式 Agent 循环。仅 Chat 类 Provider 需要实现。</summary>
    public virtual IAsyncEnumerable<ChatStreamItem> AgentStreamAsync(MicroChatContext ctx) =>
        throw new NotImplementedException("AgentStreamAsync is only supported by ChatModelClient.");

    /// <summary>批量嵌入。仅 Embedding 类 Provider 需要实现。</summary>
    public virtual Task<IReadOnlyList<Embedding<float>>> EmbedBatchAsync(MicroChatContext ctx, IReadOnlyList<string> inputs) =>
        throw new NotImplementedException("EmbedBatchAsync is only supported by EmbeddingModelClient.");

    /// <summary>记录本次模型调用的 usage（可选 cached input）。</summary>
    protected async Task TrackUsageAsync(MicroChatContext ctx, long inputTokens, long outputTokens = 0L, long cachedInputTokens = 0L)
    {
        int day = TimeUtils.TodayDay();
        string sessionId = ctx.Session.Id;
        string source = ctx.Source;
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{day}:{Id}:{ctx.Session.Id}:{ctx.Source}"))).ToLowerInvariant();
        var pricing = Config.Pricing;
        long nonCachedInput = Math.Max(0L, inputTokens - cachedInputTokens);
        decimal inputCostUsd = 0, outputCostUsd = 0, cacheInputCostUsd = 0;
        // inputCostUsd = Math.Round(nonCachedInput * pricing.InputPerMillionTokens.Value * PerMillionFactor, 8); 这个可以保留八位
        if (nonCachedInput > 0 && pricing.InputPerMillionTokens.HasValue)
            inputCostUsd = nonCachedInput * pricing.InputPerMillionTokens.Value * PerMillionFactor;
        
        if (outputTokens > 0 && pricing.OutputPerMillionTokens.HasValue)
            outputCostUsd = outputTokens * pricing.OutputPerMillionTokens.Value * PerMillionFactor;

        if (cachedInputTokens > 0 && pricing.CachedInputPerMillionTokens.HasValue)
            cacheInputCostUsd = cachedInputTokens * pricing.CachedInputPerMillionTokens.Value * PerMillionFactor;

        var existing = (await GlobalDatabase.QueryAsync<TokenDailyEntity>(e => e.Id == id, ctx.Ct)).FirstOrDefault();
        if (existing is not null)
        {
            existing.InputTokens += inputTokens;
            existing.OutputTokens += outputTokens;
            existing.CachedInputTokens += cachedInputTokens;
            existing.InputCostUsd += inputCostUsd;
            existing.OutputCostUsd += outputCostUsd;
            existing.CacheInputCostUsd += cacheInputCostUsd;
            existing.UpdatedAtMs = TimeUtils.NowMs();
            await GlobalDatabase.UpdateAsync(existing, ctx.Ct);
        }
        else
        {
            await GlobalDatabase.AddAsync(new TokenDailyEntity
            {
                Id = id,
                DayNumber = day,
                ProviderId = Id,
                ProviderName = DisplayName,
                SessionId = sessionId ?? string.Empty,
                Source = source,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CachedInputTokens = cachedInputTokens,
                InputCostUsd = inputCostUsd,
                OutputCostUsd = outputCostUsd,
                CacheInputCostUsd = cacheInputCostUsd,
                UpdatedAtMs = TimeUtils.NowMs()
            }, ctx.Ct);
        }
    }

    /// <summary>
    /// 刷新一下
    /// </summary>
    public virtual void RefreshClient() { }
}
