
namespace MicroClaw.Configuration.Options;

/// <summary>
/// 单个模型提供方的持久化配置实体。
/// </summary>
public sealed record ProviderConfigEntity
{
    /// <summary>
    /// Provider 的唯一标识。
    /// </summary>
    [YamlMember(Alias = "id", Description = "Provider 的唯一标识。")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Provider 的展示名称。
    /// </summary>
    [YamlMember(Alias = "display_name", Description = "Provider 的展示名称。")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Provider 协议类型，例如 openai 或 claude。
    /// </summary>
    [YamlMember(Alias = "protocol", Description = "Provider 协议类型，例如 openai 或 claude。")]
    public string Protocol { get; set; } = string.Empty;

    /// <summary>
    /// 模型类型，例如 chat、embedding 等。
    /// </summary>
    [YamlMember(Alias = "model_type", Description = "模型类型，例如 chat、embedding 等。")]
    public string ModelType { get; set; } = "chat";

    /// <summary>
    /// 自定义 API 基础地址。
    /// </summary>
    [YamlMember(Alias = "base_url", Description = "自定义 API 基础地址。")]
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Provider 使用的 API Key。
    /// </summary>
    [YamlMember(Alias = "api_key", Description = "Provider 使用的 API Key。")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 实际调用的模型名称。
    /// </summary>
    [YamlMember(Alias = "model_name", Description = "实际调用的模型名称。")]
    public string ModelName { get; set; } = string.Empty;

    /// <summary>
    /// 单次输出的最大 Token 数。
    /// </summary>
    [YamlMember(Alias = "max_output_tokens", Description = "单次输出的最大 Token 数。")]
    public int MaxOutputTokens { get; set; } = 8192;

    /// <summary>
    /// 指示该 Provider 是否启用。
    /// </summary>
    [YamlMember(Alias = "is_enabled", Description = "指示该 Provider 是否启用。")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 指示该 Provider 是否为默认 Provider。
    /// </summary>
    [YamlMember(Alias = "is_default", Description = "指示该 Provider 是否为默认 Provider。")]
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// Provider 能力描述，使用 JSON 字符串持久化。
    /// </summary>
    [YamlMember(Alias = "capabilities_json", Description = "Provider 能力描述，使用 JSON 字符串持久化。")]
    public string? CapabilitiesJson { get; set; }
}
