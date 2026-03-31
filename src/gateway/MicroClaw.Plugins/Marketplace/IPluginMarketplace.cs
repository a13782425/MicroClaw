using MicroClaw.Plugins.Models;

namespace MicroClaw.Plugins.Marketplace;

/// <summary>
/// Adapter interface for a specific marketplace format (e.g. Claude, Copilot).
/// Each marketplace type has its own index format and plugin resolution strategy.
/// </summary>
public interface IPluginMarketplace
{
    /// <summary>Marketplace type identifier, e.g. "claude" or "copilot".</summary>
    string Type { get; }

    /// <summary>Checks whether the given directory matches this marketplace format.</summary>
    bool CanHandle(string marketplaceDir);

    /// <summary>Lists all plugins declared in the marketplace index.</summary>
    Task<IReadOnlyList<MarketplacePluginEntry>> ListPluginsAsync(string rootPath, CancellationToken ct = default);

    /// <summary>Finds a specific plugin by name in the marketplace index.</summary>
    Task<MarketplacePluginEntry?> FindPluginAsync(string rootPath, string pluginName, CancellationToken ct = default);

    /// <summary>
    /// Resolves a marketplace plugin entry to a local directory ready for use.
    /// Depending on the source type this may copy from the marketplace repo, git clone, etc.
    /// Returns the path to the resolved plugin directory.
    /// </summary>
    Task<string> ResolvePluginSourceAsync(
        string marketplaceRootPath,
        MarketplacePluginEntry entry,
        string targetDir,
        CancellationToken ct = default);
}
