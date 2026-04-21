using MicroClaw.Configuration.Options;
namespace MicroClaw.Providers;
public enum ProviderProtocol
{
    OpenAI,
    Anthropic
}
public enum ModelType
{
    Chat,
    Embedding
}
public sealed record ProviderConfig
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public ProviderProtocol Protocol { get; init; } = ProviderProtocol.OpenAI;
    public ModelType ModelType { get; init; } = ModelType.Chat;
    public string? BaseUrl { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public int MaxOutputTokens { get; init; } = 8192;
    public bool IsEnabled { get; init; } = true;
    public bool IsDefault { get; init; } = false;
    public ProviderCapabilities Capabilities { get; init; } = new();
}
