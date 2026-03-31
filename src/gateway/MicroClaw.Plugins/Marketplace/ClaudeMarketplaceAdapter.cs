using System.Text.Json;
using MicroClaw.Plugins.Models;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Plugins.Marketplace;

/// <summary>
/// Adapter for the Claude marketplace format.
/// Reads <c>.claude-plugin/marketplace.json</c> and resolves plugins
/// based on their source type (local path, URL, or git-subdir).
/// </summary>
public sealed class ClaudeMarketplaceAdapter : IPluginMarketplace
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ILogger<ClaudeMarketplaceAdapter> _logger;

    public ClaudeMarketplaceAdapter(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<ClaudeMarketplaceAdapter>();
    }

    public string Type => "claude";

    public bool CanHandle(string marketplaceDir)
    {
        string manifestPath = Path.Combine(marketplaceDir, ".claude-plugin", "marketplace.json");
        return File.Exists(manifestPath);
    }

    public async Task<IReadOnlyList<MarketplacePluginEntry>> ListPluginsAsync(string rootPath, CancellationToken ct = default)
    {
        string manifestPath = Path.Combine(rootPath, ".claude-plugin", "marketplace.json");
        if (!File.Exists(manifestPath))
            return [];

        string json = await File.ReadAllTextAsync(manifestPath, ct);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("plugins", out JsonElement pluginsArray))
            return [];

        var entries = new List<MarketplacePluginEntry>();

        foreach (JsonElement plugin in pluginsArray.EnumerateArray())
        {
            try
            {
                MarketplacePluginEntry? entry = ParsePluginEntry(plugin);
                if (entry is not null)
                    entries.Add(entry);
            }
            catch (Exception ex)
            {
                string name = plugin.TryGetProperty("name", out JsonElement n) ? n.GetString() ?? "unknown" : "unknown";
                _logger.LogWarning(ex, "Failed to parse Claude marketplace plugin entry: {Name}", name);
            }
        }

        return entries.AsReadOnly();
    }

    public async Task<MarketplacePluginEntry?> FindPluginAsync(string rootPath, string pluginName, CancellationToken ct = default)
    {
        IReadOnlyList<MarketplacePluginEntry> plugins = await ListPluginsAsync(rootPath, ct);
        return plugins.FirstOrDefault(p => string.Equals(p.Name, pluginName, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> ResolvePluginSourceAsync(
        string marketplaceRootPath,
        MarketplacePluginEntry entry,
        string targetDir,
        CancellationToken ct = default)
    {
        switch (entry.Source.SourceType)
        {
            case MarketplacePluginSourceType.Local:
            {
                // Local: copy from the marketplace repo
                string sourcePath = entry.Source.Path ?? throw new InvalidOperationException("Local source requires a path.");
                string fullSource = Path.GetFullPath(Path.Combine(marketplaceRootPath, sourcePath));

                if (!fullSource.StartsWith(Path.GetFullPath(marketplaceRootPath), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Path traversal detected in local plugin source.");

                if (!Directory.Exists(fullSource))
                    throw new InvalidOperationException($"Local plugin source directory not found: {fullSource}");

                GitHelper.CopyDirectory(fullSource, targetDir);
                return targetDir;
            }

            case MarketplacePluginSourceType.Url:
            {
                // URL: git clone the repository
                string url = entry.Source.Url ?? throw new InvalidOperationException("URL source requires a url.");

                if (entry.Source.Path is not null)
                {
                    // URL with path: clone to temp, then copy subdir
                    string tempDir = Path.Combine(Path.GetTempPath(), $"microclaw-marketplace-{Guid.NewGuid():N}");
                    try
                    {
                        await GitHelper.CloneAsync(url, entry.Source.Ref, tempDir, ct);
                        string subDir = Path.GetFullPath(Path.Combine(tempDir, entry.Source.Path));
                        if (!subDir.StartsWith(Path.GetFullPath(tempDir), StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Path traversal detected in plugin source path.");
                        if (!Directory.Exists(subDir))
                            throw new InvalidOperationException($"Subdirectory '{entry.Source.Path}' not found in cloned repo.");
                        GitHelper.CopyDirectory(subDir, targetDir);
                    }
                    finally
                    {
                        if (Directory.Exists(tempDir))
                            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
                    }
                }
                else
                {
                    await GitHelper.CloneAsync(url, entry.Source.Ref, targetDir, ct);
                }

                return targetDir;
            }

            case MarketplacePluginSourceType.GitSubdir:
            {
                // GitSubdir: clone repo, copy subdirectory
                string url = entry.Source.Url ?? throw new InvalidOperationException("GitSubdir source requires a url.");
                string subPath = entry.Source.Path ?? throw new InvalidOperationException("GitSubdir source requires a path.");

                // Handle short-form GitHub URLs (e.g. "owner/repo")
                string gitUrl = url.Contains("://") || url.Contains('@')
                    ? url
                    : $"https://github.com/{url}.git";

                string tempDir = Path.Combine(Path.GetTempPath(), $"microclaw-marketplace-{Guid.NewGuid():N}");
                try
                {
                    await GitHelper.CloneAsync(gitUrl, entry.Source.Ref, tempDir, ct);
                    string subDir = Path.GetFullPath(Path.Combine(tempDir, subPath));

                    if (!subDir.StartsWith(Path.GetFullPath(tempDir), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Path traversal detected in git-subdir path.");

                    if (!Directory.Exists(subDir))
                        throw new InvalidOperationException($"Subdirectory '{subPath}' not found in cloned repo.");

                    GitHelper.CopyDirectory(subDir, targetDir);
                }
                finally
                {
                    if (Directory.Exists(tempDir))
                        try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
                }

                return targetDir;
            }

            default:
                throw new NotSupportedException($"Unsupported source type: {entry.Source.SourceType}");
        }
    }

    // ── Parsing ─────────────────────────────────────────────────────────────

    private static MarketplacePluginEntry? ParsePluginEntry(JsonElement el)
    {
        if (!el.TryGetProperty("name", out JsonElement nameEl))
            return null;

        string? name = nameEl.GetString();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        MarketplacePluginSource source = ParseSource(el);

        return new MarketplacePluginEntry
        {
            Name = name,
            Description = el.TryGetProperty("description", out JsonElement d) ? d.GetString() : null,
            Category = el.TryGetProperty("category", out JsonElement c) ? c.GetString() : null,
            Author = ParseAuthor(el),
            Homepage = el.TryGetProperty("homepage", out JsonElement h) ? h.GetString() : null,
            Version = el.TryGetProperty("version", out JsonElement v) ? v.GetString() : null,
            Keywords = ParseStringArray(el, "keywords"),
            Source = source,
            Tags = ParseStringArray(el, "tags")
        };
    }

    private static MarketplacePluginSource ParseSource(JsonElement pluginEl)
    {
        if (!pluginEl.TryGetProperty("source", out JsonElement sourceEl))
            throw new JsonException("Plugin entry missing 'source' field.");

        // String form: "./plugins/xxx" or "./external_plugins/xxx"
        if (sourceEl.ValueKind == JsonValueKind.String)
        {
            string path = sourceEl.GetString()!;
            return new MarketplacePluginSource
            {
                SourceType = MarketplacePluginSourceType.Local,
                Path = path
            };
        }

        // Object form: { source: "url"|"git-subdir"|"github", ... }
        if (sourceEl.ValueKind == JsonValueKind.Object)
        {
            string? sourceType = sourceEl.TryGetProperty("source", out JsonElement st) ? st.GetString() : null;
            string? url = sourceEl.TryGetProperty("url", out JsonElement u) ? u.GetString() : null;
            string? path = sourceEl.TryGetProperty("path", out JsonElement p) ? p.GetString() : null;
            string? gitRef = sourceEl.TryGetProperty("ref", out JsonElement r) ? r.GetString() : null;
            string? sha = sourceEl.TryGetProperty("sha", out JsonElement s) ? s.GetString() : null;
            string? repo = sourceEl.TryGetProperty("repo", out JsonElement rp) ? rp.GetString() : null;

            return sourceType switch
            {
                "url" => new MarketplacePluginSource
                {
                    SourceType = MarketplacePluginSourceType.Url,
                    Url = url,
                    Path = path,
                    Ref = gitRef,
                    Sha = sha
                },
                "git-subdir" => new MarketplacePluginSource
                {
                    SourceType = MarketplacePluginSourceType.GitSubdir,
                    Url = url,
                    Path = path,
                    Ref = gitRef,
                    Sha = sha
                },
                "github" => new MarketplacePluginSource
                {
                    SourceType = MarketplacePluginSourceType.GitHub,
                    Repo = repo,
                    Path = path
                },
                _ => throw new JsonException($"Unknown source type: {sourceType}")
            };
        }

        throw new JsonException($"Unexpected source field type: {sourceEl.ValueKind}");
    }

    private static PluginAuthor? ParseAuthor(JsonElement pluginEl)
    {
        if (!pluginEl.TryGetProperty("author", out JsonElement authorEl))
            return null;

        if (authorEl.ValueKind == JsonValueKind.String)
            return new PluginAuthor { Name = authorEl.GetString() };

        if (authorEl.ValueKind == JsonValueKind.Object)
        {
            return new PluginAuthor
            {
                Name = authorEl.TryGetProperty("name", out JsonElement n) ? n.GetString() : null,
                Email = authorEl.TryGetProperty("email", out JsonElement e) ? e.GetString() : null,
                Url = authorEl.TryGetProperty("url", out JsonElement u) ? u.GetString() : null
            };
        }

        return null;
    }

    private static IReadOnlyList<string>? ParseStringArray(JsonElement el, string propertyName)
    {
        if (!el.TryGetProperty(propertyName, out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
            return null;

        var list = new List<string>();
        foreach (JsonElement item in arr.EnumerateArray())
        {
            string? val = item.GetString();
            if (val is not null)
                list.Add(val);
        }

        return list.Count > 0 ? list.AsReadOnly() : null;
    }
}
