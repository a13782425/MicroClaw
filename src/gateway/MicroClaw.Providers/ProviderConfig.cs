namespace MicroClaw.Providers;

public enum ProviderProtocol
{
    OpenAI,
    Anthropic
}

/// <summary>
/// Provider 的能力描述，包含输入/输出模态、特殊功能及价格信息。
/// 以 JSON 形式持久化到数据库，便于后续扩展新字段。
/// </summary>
public sealed record ProviderCapabilities
{
    // 输入模态
    public bool InputText { get; init; } = true;
    public bool InputImage { get; init; }
    public bool InputAudio { get; init; }
    public bool InputVideo { get; init; }
    public bool InputFile { get; init; }

    // 输出模态
    public bool OutputText { get; init; } = true;
    public bool OutputImage { get; init; }
    public bool OutputAudio { get; init; }
    public bool OutputVideo { get; init; }

    // 特殊能力
    public bool SupportsFunctionCalling { get; init; }
    public bool SupportsResponsesApi { get; init; }

    // 价格（$/1M tokens）
    public decimal? InputPricePerMToken { get; init; }
    public decimal? OutputPricePerMToken { get; init; }
    public decimal? CacheInputPricePerMToken { get; init; }
    public decimal? CacheOutputPricePerMToken { get; init; }

    // 备注
    public string? Notes { get; init; }
}

public sealed record ProviderConfig
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public ProviderProtocol Protocol { get; init; } = ProviderProtocol.OpenAI;
    public string? BaseUrl { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public int MaxOutputTokens { get; init; } = 8192;
    public bool IsEnabled { get; init; } = true;
    public bool IsDefault { get; init; } = false;
    public ProviderCapabilities Capabilities { get; init; } = new();
}
