using Microsoft.Extensions.Configuration;

namespace MicroClaw.Configuration.Options;

public sealed record ProviderConfigEntity
{
    [ConfigurationKeyName("id")]
    public string Id { get; set; } = string.Empty;

    [ConfigurationKeyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [ConfigurationKeyName("protocol")]
    public string Protocol { get; set; } = string.Empty;

    [ConfigurationKeyName("model_type")]
    public string ModelType { get; set; } = "chat";

    [ConfigurationKeyName("base_url")]
    public string? BaseUrl { get; set; }

    [ConfigurationKeyName("api_key")]
    public string ApiKey { get; set; } = string.Empty;

    [ConfigurationKeyName("model_name")]
    public string ModelName { get; set; } = string.Empty;

    [ConfigurationKeyName("max_output_tokens")]
    public int MaxOutputTokens { get; set; } = 8192;

    [ConfigurationKeyName("is_enabled")]
    public bool IsEnabled { get; set; } = true;

    [ConfigurationKeyName("is_default")]
    public bool IsDefault { get; set; } = false;

    [ConfigurationKeyName("capabilities_json")]
    public string? CapabilitiesJson { get; set; }
}
