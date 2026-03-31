using MicroClaw.Plugins.Models;

namespace MicroClaw.Plugins.Marketplace;

/// <summary>
/// Manages registered plugin marketplaces and supports installing plugins from them.
/// </summary>
public interface IMarketplaceManager
{
    IReadOnlyList<MarketplaceInfo> GetAll();
    MarketplaceInfo? GetByName(string name);

    Task<MarketplaceInfo> AddAsync(PluginSource source, CancellationToken ct = default);
    Task<MarketplaceInfo> AddFromDirectoryAsync(string clonedDir, PluginSource source, CancellationToken ct = default);
    Task RemoveAsync(string name, CancellationToken ct = default);
    Task<MarketplaceInfo> UpdateAsync(string name, CancellationToken ct = default);

    Task<IReadOnlyList<MarketplacePluginEntry>> ListPluginsAsync(string marketplaceName, CancellationToken ct = default);
    Task<IReadOnlyList<MarketplacePluginEntry>> SearchPluginsAsync(string? keyword, string? category, CancellationToken ct = default);
    Task<PluginInfo> InstallPluginAsync(string marketplaceName, string pluginName, CancellationToken ct = default);
}
