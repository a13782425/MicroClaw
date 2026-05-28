using YamlDotNet.Serialization;
namespace MicroClaw.Configuration;

/// <summary>
/// 单个模型提供方的持久化配置（YAML 表示）。所有结构化字段使用字符串/列表，
/// 由 <c>MicroClaw.Providers</c> 中的 Mapper 解析为强类型 <c>ModelProfile</c>。
/// </summary>
public sealed record ProviderEntityConfig
{
    /// <summary>Provider 的唯一标识。</summary>
    [YamlMember(Alias = "id", Description = "Provider 的唯一标识。")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Provider 的展示名称。</summary>
    [YamlMember(Alias = "display_name", Description = "Provider 的展示名称。")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>API 协议族：openai | anthropic | other。</summary>
    [YamlMember(Alias = "api_kind", Description = "API 协议族：openai | anthropic | other。")]
    public string ApiKind { get; set; } = "openai";

    /// <summary>模型用途：chat | embedding。</summary>
    [YamlMember(Alias = "model_kind", Description = "模型用途：chat | embedding。")]
    public string ModelKind { get; set; } = "chat";

    /// <summary>自定义 API 基础地址；为空时使用 SDK 默认端点。</summary>
    [YamlMember(Alias = "base_url", Description = "自定义 API 基础地址；为空时使用 SDK 默认端点。")]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Provider 使用的 API Key，支持 ${ENV_VAR} 占位符。</summary>
    [YamlMember(Alias = "api_key", Description = "Provider 使用的 API Key，支持 ${ENV_VAR} 占位符。")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>实际调用的模型名称。</summary>
    [YamlMember(Alias = "model_name", Description = "实际调用的模型名称。")]
    public string ModelName { get; set; } = string.Empty;

    /// <summary>单次输出的最大 Token 数。</summary>
    [YamlMember(Alias = "max_output_tokens", Description = "单次输出的最大 Token 数。")]
    public int MaxOutputTokens { get; set; } = 8192;
    /// <summary>    
    /// 单次调用的最大上下文长
    /// </summary>
    [YamlMember(Alias = "max_context_length", Description = "最大上下文长度")]
    public long MaxContextLength { get; set; } = 128000;

    /// <summary>能力开关：tool_calling | responses_api。</summary>
    [YamlMember(Alias = "capabilities", Description = "能力开关：tool_calling | responses_api。")]
    public List<string> Capabilities { get; set; } = [];

    /// <summary>支持的输入模态：text | image | audio | video | file。</summary>
    [YamlMember(Alias = "input_modalities", Description = "支持的输入模态：text | image | audio | video | file。")]
    public List<string> InputModalities { get; set; } = ["text"];

    /// <summary>支持的输出模态：text | image | audio | video | file。</summary>
    [YamlMember(Alias = "output_modalities", Description = "支持的输出模态：text | image | audio | video | file。")]
    public List<string> OutputModalities { get; set; } = ["text"];

    /// <summary>价格信息（按每百万 Token 美元定价）。</summary>
    [YamlMember(Alias = "pricing", Description = "价格信息（按每百万 Token 美元定价）。")]
    public ProviderPricingConfig Pricing { get; set; } = new();

    /// <summary>场景评分映射：key 为场景名称，value 为 {score, notes?}。</summary>
    [YamlMember(Alias = "scenario_scores", Description = "场景评分映射：key 为场景名称，value 为 {score, notes?}。")]
    public Dictionary<string, ProviderScenarioScoreConfig> ScenarioScores { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>是否启用该 Provider。</summary>
    [YamlMember(Alias = "is_enabled", Description = "是否启用该 Provider。")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>是否为默认 Provider（同 model_kind 下仅一个生效）。</summary>
    [YamlMember(Alias = "is_default", Description = "是否为默认 Provider（同 model_kind 下仅一个生效）。")]
    public bool IsDefault { get; set; }
}

/// <summary>价格 YAML 配置块。</summary>
public sealed record ProviderPricingConfig
{
    [YamlMember(Alias = "input_per_million_tokens", Description = "常规输入 Token 单价（每百万 tokens）。")]
    public decimal? InputPerMillionTokens { get; set; }

    [YamlMember(Alias = "output_per_million_tokens", Description = "输出 Token 单价（每百万 tokens）。")]
    public decimal? OutputPerMillionTokens { get; set; }

    [YamlMember(Alias = "cached_input_per_million_tokens", Description = "命中缓存的输入 Token 单价（每百万 tokens）；缺省回退 input。")]
    public decimal? CachedInputPerMillionTokens { get; set; }

    [YamlMember(Alias = "cached_output_per_million_tokens", Description = "命中缓存的输出 Token 单价（每百万 tokens）。")]
    public decimal? CachedOutputPerMillionTokens { get; set; }
}

/// <summary>单个场景评分 YAML 配置块。</summary>
public sealed record ProviderScenarioScoreConfig
{
    [YamlMember(Alias = "score", Description = "场景评分 0-100，默认 50。")]
    public int Score { get; set; } = 50;

    [YamlMember(Alias = "notes", Description = "可选备注。")]
    public string? Notes { get; set; }
}
