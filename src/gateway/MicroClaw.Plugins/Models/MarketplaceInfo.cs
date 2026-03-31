namespace MicroClaw.Plugins.Models;

/// <summary>
/// Represents a registered plugin marketplace (e.g. Claude's official marketplace, Copilot's awesome-copilot).
/// </summary>
public sealed record MarketplaceInfo
{
    public required string Name { get; init; }
    public required string RootPath { get; init; }
    public required string MarketplaceType { get; init; } // "claude", "copilot"
    public required PluginSource Source { get; init; }
    public DateTimeOffset RegisteredAt { get; init; }
}
