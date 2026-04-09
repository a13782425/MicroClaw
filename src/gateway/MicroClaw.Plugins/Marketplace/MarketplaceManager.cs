using System.Collections.Concurrent;
using System.Text.Json;
using MicroClaw.Abstractions;
using MicroClaw.Configuration;
using MicroClaw.Plugins.Models;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Plugins.Marketplace;

/// <summary>
/// Manages registered plugin marketplaces and supports browsing/installing plugins from them.
/// Implements <see cref="IHostedService"/> to initialize on startup.
/// </summary>
public sealed class MarketplaceManager : IMarketplaceManager, IService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _marketplaceDir;
    private readonly string _registryPath;
    private readonly string _pluginsDir;
    private readonly IEnumerable<IPluginMarketplace> _adapters;
    private readonly IPluginRegistry _pluginRegistry;
    private readonly ILogger<MarketplaceManager> _logger;
    private readonly ConcurrentDictionary<string, MarketplaceInfo> _marketplaces = new(StringComparer.OrdinalIgnoreCase);

    public MarketplaceManager(
        IEnumerable<IPluginMarketplace> adapters,
        IPluginRegistry pluginRegistry,
        ILoggerFactory loggerFactory)
    {
        _adapters = adapters;
        _pluginRegistry = pluginRegistry;
        _logger = loggerFactory.CreateLogger<MarketplaceManager>();
        _marketplaceDir = Path.Combine(MicroClawConfig.Env.WorkspaceRoot, "marketplace");
        _registryPath = Path.Combine(_marketplaceDir, "marketplace-registry.json");
        _pluginsDir = Path.Combine(MicroClawConfig.Env.WorkspaceRoot, "plugins");
    }

    // ── IService ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public int InitOrder => 30;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_marketplaceDir);
        await LoadRegisteredMarketplacesAsync(cancellationToken);
        _logger.LogInformation("Marketplace system initialized: {Count} marketplaces loaded", _marketplaces.Count);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── IMarketplaceManager ─────────────────────────────────────────────────

    public IReadOnlyList<MarketplaceInfo> GetAll() =>
        _marketplaces.Values.ToList().AsReadOnly();

    public MarketplaceInfo? GetByName(string name) =>
        _marketplaces.GetValueOrDefault(name);

    public async Task<MarketplaceInfo> AddAsync(PluginSource source, CancellationToken ct = default)
    {
        if (source.Type != "git" || string.IsNullOrWhiteSpace(source.Url))
            throw new ArgumentException("Marketplace source must be a git URL.");

        string repoName = GitHelper.ExtractRepoName(source.Url);

        if (_marketplaces.ContainsKey(repoName))
            throw new InvalidOperationException($"Marketplace '{repoName}' is already registered.");

        string targetDir = Path.Combine(_marketplaceDir, repoName);

        if (Directory.Exists(targetDir))
            throw new InvalidOperationException($"Marketplace directory '{repoName}' already exists.");

        await GitHelper.CloneAsync(source.Url, source.Ref, targetDir, ct);

        // Detect marketplace type
        IPluginMarketplace? adapter = _adapters.FirstOrDefault(a => a.CanHandle(targetDir));
        string marketplaceType = adapter?.Type ?? "unknown";

        if (adapter is null)
            _logger.LogWarning("No adapter recognized marketplace at '{Dir}'. Registered as 'unknown' type.", targetDir);

        var info = new MarketplaceInfo
        {
            Name = repoName,
            RootPath = targetDir,
            MarketplaceType = marketplaceType,
            Source = source,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        // Persist
        var entry = new MarketplaceRegistryEntry
        {
            Source = source,
            MarketplaceType = marketplaceType,
            RegisteredAt = info.RegisteredAt
        };
        await AddRegistryEntryAsync(repoName, entry);
        _marketplaces[repoName] = info;

        _logger.LogInformation("Marketplace '{Name}' registered (type: {Type})", repoName, marketplaceType);
        return info;
    }

    public async Task<MarketplaceInfo> AddFromDirectoryAsync(string clonedDir, PluginSource source, CancellationToken ct = default)
    {
        if (!Directory.Exists(clonedDir))
            throw new ArgumentException($"Directory not found: {clonedDir}");

        string repoName = Path.GetFileName(clonedDir);

        if (_marketplaces.ContainsKey(repoName))
            throw new InvalidOperationException($"Marketplace '{repoName}' is already registered.");

        string targetDir = Path.Combine(_marketplaceDir, repoName);

        if (Directory.Exists(targetDir))
            throw new InvalidOperationException($"Marketplace directory '{repoName}' already exists.");

        Directory.CreateDirectory(_marketplaceDir);
        Directory.Move(clonedDir, targetDir);

        // Detect marketplace type
        IPluginMarketplace? adapter = _adapters.FirstOrDefault(a => a.CanHandle(targetDir));
        string marketplaceType = adapter?.Type ?? "unknown";

        if (adapter is null)
            _logger.LogWarning("No adapter recognized marketplace at '{Dir}'. Registered as 'unknown' type.", targetDir);

        var info = new MarketplaceInfo
        {
            Name = repoName,
            RootPath = targetDir,
            MarketplaceType = marketplaceType,
            Source = source,
            RegisteredAt = DateTimeOffset.UtcNow
        };

        var entry = new MarketplaceRegistryEntry
        {
            Source = source,
            MarketplaceType = marketplaceType,
            RegisteredAt = info.RegisteredAt
        };
        await AddRegistryEntryAsync(repoName, entry);
        _marketplaces[repoName] = info;

        _logger.LogInformation("Marketplace '{Name}' registered from pre-cloned directory (type: {Type})", repoName, marketplaceType);
        return info;
    }

    public async Task RemoveAsync(string name, CancellationToken ct = default)
    {
        if (!_marketplaces.TryRemove(name, out MarketplaceInfo? info))
            throw new InvalidOperationException($"Marketplace '{name}' not found.");

        await RemoveRegistryEntryAsync(name);

        if (Directory.Exists(info.RootPath))
        {
            try
            {
                Directory.Delete(info.RootPath, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete marketplace directory: {Path}", info.RootPath);
            }
        }

        _logger.LogInformation("Marketplace '{Name}' removed", name);
    }

    public async Task<MarketplaceInfo> UpdateAsync(string name, CancellationToken ct = default)
    {
        if (!_marketplaces.TryGetValue(name, out MarketplaceInfo? info))
            throw new InvalidOperationException($"Marketplace '{name}' not found.");

        await GitHelper.PullAsync(info.RootPath, ct);

        _logger.LogInformation("Marketplace '{Name}' updated", name);
        return info;
    }

    public async Task<IReadOnlyList<MarketplacePluginEntry>> ListPluginsAsync(string marketplaceName, CancellationToken ct = default)
    {
        if (!_marketplaces.TryGetValue(marketplaceName, out MarketplaceInfo? info))
            throw new InvalidOperationException($"Marketplace '{marketplaceName}' not found.");

        IPluginMarketplace adapter = GetAdapter(info.MarketplaceType);
        return await adapter.ListPluginsAsync(info.RootPath, ct);
    }

    public async Task<IReadOnlyList<MarketplacePluginEntry>> SearchPluginsAsync(string? keyword, string? category, CancellationToken ct = default)
    {
        var results = new List<MarketplacePluginEntry>();

        foreach (MarketplaceInfo info in _marketplaces.Values)
        {
            IPluginMarketplace? adapter = _adapters.FirstOrDefault(a => a.Type == info.MarketplaceType);
            if (adapter is null) continue;

            try
            {
                IReadOnlyList<MarketplacePluginEntry> plugins = await adapter.ListPluginsAsync(info.RootPath, ct);

                IEnumerable<MarketplacePluginEntry> filtered = plugins;

                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    filtered = filtered.Where(p =>
                        (p.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true) ||
                        (p.Description?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true) ||
                        (p.Keywords?.Any(k => k.Contains(keyword, StringComparison.OrdinalIgnoreCase)) == true));
                }

                if (!string.IsNullOrWhiteSpace(category))
                {
                    filtered = filtered.Where(p =>
                        string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase));
                }

                results.AddRange(filtered);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to list plugins from marketplace '{Name}'", info.Name);
            }
        }

        return results.AsReadOnly();
    }

    public async Task<PluginInfo> InstallPluginAsync(string marketplaceName, string pluginName, CancellationToken ct = default)
    {
        if (!_marketplaces.TryGetValue(marketplaceName, out MarketplaceInfo? info))
            throw new InvalidOperationException($"Marketplace '{marketplaceName}' not found.");

        IPluginMarketplace adapter = GetAdapter(info.MarketplaceType);

        MarketplacePluginEntry? entry = await adapter.FindPluginAsync(info.RootPath, pluginName, ct);
        if (entry is null)
            throw new InvalidOperationException($"Plugin '{pluginName}' not found in marketplace '{marketplaceName}'.");

        string targetDir = Path.Combine(_pluginsDir, pluginName);
        if (Directory.Exists(targetDir))
            throw new InvalidOperationException($"Plugin directory '{pluginName}' already exists. Uninstall it first.");

        Directory.CreateDirectory(_pluginsDir);
        await adapter.ResolvePluginSourceAsync(info.RootPath, entry, targetDir, ct);

        // Reload the plugin registry so it picks up the new plugin
        await _pluginRegistry.ReloadAsync(ct);

        PluginInfo? installed = _pluginRegistry.GetByName(pluginName);
        if (installed is null)
            throw new InvalidOperationException($"Plugin '{pluginName}' was installed but could not be loaded.");

        _logger.LogInformation("Plugin '{Plugin}' installed from marketplace '{Marketplace}'", pluginName, marketplaceName);
        return installed;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private IPluginMarketplace GetAdapter(string marketplaceType)
    {
        return _adapters.FirstOrDefault(a => a.Type == marketplaceType)
            ?? throw new InvalidOperationException($"No adapter found for marketplace type '{marketplaceType}'.");
    }

    private async Task LoadRegisteredMarketplacesAsync(CancellationToken ct)
    {
        MarketplaceRegistryFile registry = await LoadRegistryAsync();

        foreach ((string name, MarketplaceRegistryEntry entry) in registry.Marketplaces)
        {
            string rootPath = Path.Combine(_marketplaceDir, name);
            if (!Directory.Exists(rootPath))
            {
                _logger.LogWarning("Marketplace '{Name}' registered but directory not found: {Path}", name, rootPath);
                continue;
            }

            _marketplaces[name] = new MarketplaceInfo
            {
                Name = name,
                RootPath = rootPath,
                MarketplaceType = entry.MarketplaceType,
                Source = entry.Source,
                RegisteredAt = entry.RegisteredAt
            };
        }
    }

    // ── Registry File Operations ────────────────────────────────────────────

    private async Task<MarketplaceRegistryFile> LoadRegistryAsync()
    {
        if (!File.Exists(_registryPath))
            return new MarketplaceRegistryFile();

        try
        {
            string json = await File.ReadAllTextAsync(_registryPath);
            return JsonSerializer.Deserialize<MarketplaceRegistryFile>(json, JsonOpts) ?? new MarketplaceRegistryFile();
        }
        catch
        {
            return new MarketplaceRegistryFile();
        }
    }

    private async Task SaveRegistryAsync(MarketplaceRegistryFile registry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_registryPath)!);
        string json = JsonSerializer.Serialize(registry, JsonOpts);
        await File.WriteAllTextAsync(_registryPath, json);
    }

    private async Task AddRegistryEntryAsync(string name, MarketplaceRegistryEntry entry)
    {
        MarketplaceRegistryFile registry = await LoadRegistryAsync();
        registry.Marketplaces[name] = entry;
        await SaveRegistryAsync(registry);
    }

    private async Task RemoveRegistryEntryAsync(string name)
    {
        MarketplaceRegistryFile registry = await LoadRegistryAsync();
        registry.Marketplaces.Remove(name);
        await SaveRegistryAsync(registry);
    }
}
