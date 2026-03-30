namespace MicroClaw.Infrastructure.Data;

public sealed class ProviderConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string ModelType { get; set; } = "chat";
    public string? BaseUrl { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public string ModelName { get; set; } = string.Empty;
    public int MaxOutputTokens { get; set; } = 8192;
    public bool IsEnabled { get; set; } = true;
    public bool IsDefault { get; set; } = false;
    public string? CapabilitiesJson { get; set; }
}
