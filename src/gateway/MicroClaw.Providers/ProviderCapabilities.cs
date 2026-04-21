using System.Text.Json.Serialization;

namespace MicroClaw.Providers;

/// <summary>Provider 支持的输入模态。Flag 枚举，按位组合。</summary>
[Flags]
[JsonConverter(typeof(InputModalityJsonConverter))]
public enum InputModality
{
    None = 0,
    Text = 1 << 0,
    Image = 1 << 1,
    Audio = 1 << 2,
    Video = 1 << 3,
    File = 1 << 4,
}

/// <summary>Provider 支持的输出模态。Flag 枚举，按位组合。</summary>
[Flags]
[JsonConverter(typeof(OutputModalityJsonConverter))]
public enum OutputModality
{
    None = 0,
    Text = 1 << 0,
    Image = 1 << 1,
    Audio = 1 << 2,
    Video = 1 << 3,
}

/// <summary>Provider 的特殊能力开关。Flag 枚举，按位组合。</summary>
[Flags]
[JsonConverter(typeof(ProviderFeatureJsonConverter))]
public enum ProviderFeature
{
    None = 0,
    FunctionCalling = 1 << 0,
    ResponsesApi = 1 << 1,
}

/// <summary>
/// Provider 的能力描述，包含输入/输出模态、特殊功能及价格信息。
/// 以 JSON 形式持久化到 providers.yaml，便于后续扩展新字段。
/// </summary>
public sealed record ProviderCapabilities
{
    /// <summary>支持的输入模态集合，默认仅 Text。</summary>
    public InputModality Inputs { get; init; } = InputModality.Text;

    /// <summary>支持的输出模态集合，默认仅 Text。</summary>
    public OutputModality Outputs { get; init; } = OutputModality.Text;

    /// <summary>特殊能力开关集合，默认无。</summary>
    public ProviderFeature Features { get; init; } = ProviderFeature.None;

    // Embedding 专用
    public int? OutputDimensions { get; init; }
    public int? MaxInputTokens { get; init; }

    // 价格（$/1M tokens）
    public decimal? InputPricePerMToken { get; init; }
    public decimal? OutputPricePerMToken { get; init; }
    public decimal? CacheInputPricePerMToken { get; init; }
    public decimal? CacheOutputPricePerMToken { get; init; }

    /// <summary>质量评分（0-100）。用于 <see cref="ProviderRoutingStrategy.QualityFirst"/> 策略。默认 50。</summary>
    public int QualityScore { get; init; } = 50;

    /// <summary>延迟层级。用于 <see cref="ProviderRoutingStrategy.LatencyFirst"/> 策略。默认 Medium。</summary>
    public LatencyTier LatencyTier { get; init; } = LatencyTier.Medium;

    public string? Notes { get; init; }
}
