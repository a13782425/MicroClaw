using System.Text.Json.Serialization;

namespace MicroClaw.Plugins.Models;

/// <summary>
/// Entry in the marketplace registry file (<c>workspace/marketplace/marketplace-registry.json</c>).
/// </summary>
public sealed record MarketplaceRegistryEntry
{
    [JsonPropertyName("source")]
    public required PluginSource Source { get; init; }

    [JsonPropertyName("marketplaceType")]
    public required string MarketplaceType { get; init; }

    [JsonPropertyName("registeredAt")]
    public DateTimeOffset RegisteredAt { get; init; }
}

/// <summary>
/// Root object of the marketplace registry file.
/// </summary>
public sealed class MarketplaceRegistryFile
{
    [JsonPropertyName("marketplaces")]
    public Dictionary<string, MarketplaceRegistryEntry> Marketplaces { get; init; } = new();
}
