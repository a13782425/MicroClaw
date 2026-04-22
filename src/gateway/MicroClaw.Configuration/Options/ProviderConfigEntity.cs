
namespace MicroClaw.Configuration.Options;

public sealed record ProviderConfigEntity
{
    [YamlMember(Alias = "id")]
    public string Id { get; set; } = string.Empty;

    [YamlMember(Alias = "display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [YamlMember(Alias = "protocol")]
    public string Protocol { get; set; } = string.Empty;

    [YamlMember(Alias = "model_type")]
    public string ModelType { get; set; } = "chat";

    [YamlMember(Alias = "base_url")]
    public string? BaseUrl { get; set; }

    [YamlMember(Alias = "api_key")]
    public string ApiKey { get; set; } = string.Empty;

    [YamlMember(Alias = "model_name")]
    public string ModelName { get; set; } = string.Empty;

    [YamlMember(Alias = "max_output_tokens")]
    public int MaxOutputTokens { get; set; } = 8192;

    [YamlMember(Alias = "is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [YamlMember(Alias = "is_default")]
    public bool IsDefault { get; set; } = false;

    [YamlMember(Alias = "capabilities_json")]
    public string? CapabilitiesJson { get; set; }
}
