namespace MicroClaw.Provider.Abstractions;

public enum ProviderProtocol
{
    OpenAI,
    OpenAIResponses,
    Anthropic
}

public sealed record ProviderConfig
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public ProviderProtocol Protocol { get; init; } = ProviderProtocol.OpenAI;
    public string? BaseUrl { get; init; }
    public string ApiKey { get; init; } = string.Empty;
    public string ModelName { get; init; } = string.Empty;
    public bool IsEnabled { get; init; } = true;
}
