using MicroClaw.Plugins.Models;

namespace MicroClaw.Plugins.Marketplace;

/// <summary>
/// Placeholder adapter for the Copilot marketplace format (awesome-copilot).
/// Detects <c>plugins/external.json</c> as the marketplace index.
/// Full implementation deferred to a future phase.
/// </summary>
public sealed class CopilotMarketplaceAdapter : IPluginMarketplace
{
    public string Type => "copilot";

    public bool CanHandle(string marketplaceDir)
    {
        string indexPath = Path.Combine(marketplaceDir, "plugins", "external.json");
        return File.Exists(indexPath);
    }

    public Task<IReadOnlyList<MarketplacePluginEntry>> ListPluginsAsync(string rootPath, CancellationToken ct = default)
        => throw new NotImplementedException("Copilot marketplace adapter is not yet implemented.");

    public Task<MarketplacePluginEntry?> FindPluginAsync(string rootPath, string pluginName, CancellationToken ct = default)
        => throw new NotImplementedException("Copilot marketplace adapter is not yet implemented.");

    public Task<string> ResolvePluginSourceAsync(string marketplaceRootPath, MarketplacePluginEntry entry, string targetDir, CancellationToken ct = default)
        => throw new NotImplementedException("Copilot marketplace adapter is not yet implemented.");
}
