namespace MicroClaw.Providers;

/// <summary>
/// 模型价格（按每百万 Token 美元定价）。所有字段可空：null 表示未配置（视为 0 计费）。
/// </summary>
public sealed record ModelPricing
{
    /// <summary>常规输入 Token 单价（每 1,000,000 tokens）。</summary>
    public decimal? InputPerMillionTokens { get; init; }

    /// <summary>输出 Token 单价（每 1,000,000 tokens）。</summary>
    public decimal? OutputPerMillionTokens { get; init; }

    /// <summary>命中缓存的输入 Token 单价；未配置时回退到 <see cref="InputPerMillionTokens"/>。</summary>
    public decimal? CachedInputPerMillionTokens { get; init; }

    /// <summary>命中缓存的输出 Token 单价；未配置视为 0。</summary>
    public decimal? CachedOutputPerMillionTokens { get; init; }

    public static ModelPricing Empty { get; } = new();
}
