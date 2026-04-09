using System.Collections.Concurrent;
using System.Text.Json;
using MicroClaw.Abstractions;
using MicroClaw.Configuration;
using MicroClaw.Abstractions.Plugins;
using MicroClaw.Plugins.Hooks;
using MicroClaw.Plugins.Models;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Plugins;

/// <summary>
/// Loads and manages plugins from <c>workspace/plugins/</c>.
/// Implements <see cref="IHostedService"/> to auto-load on startup.
/// </summary>
public sealed class PluginLoader : IPluginRegistry, IService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _pluginsDir;
    private readonly string _registryPath;
    private readonly ILogger<PluginLoader> _logger;
    private readonly ConcurrentDictionary<string, PluginInfo> _plugins = new(StringComparer.OrdinalIgnoreCase);

    private readonly IPluginSkillRegistrar? _skillRegistrar;
    private readonly IPluginMcpRegistrar? _mcpRegistrar;
    private readonly IPluginAgentRegistrar? _agentRegistrar;

    public PluginLoader(
        ILoggerFactory loggerFactory,
        IPluginSkillRegistrar? skillRegistrar = null,
        IPluginMcpRegistrar? mcpRegistrar = null,
        IPluginAgentRegistrar? agentRegistrar = null)
    {
        _logger = loggerFactory.CreateLogger<PluginLoader>();
        _pluginsDir = Path.Combine(MicroClawConfig.Env.WorkspaceRoot, "plugins");
        _registryPath = Path.Combine(_pluginsDir, "plugin.json");
        _skillRegistrar = skillRegistrar;
        _mcpRegistrar = mcpRegistrar;
        _agentRegistrar = agentRegistrar;
    }

    // ── IService ──────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public int InitOrder => 30;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_pluginsDir);
        await ReloadAsync(cancellationToken);
        _logger.LogInformation("Plugin system initialized: {Count} plugins loaded from {Dir}", _plugins.Count, _pluginsDir);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── IPluginRegistry ─────────────────────────────────────────────────────

    public IReadOnlyList<PluginInfo> GetAll() =>
        _plugins.Values.ToList().AsReadOnly();

    public IReadOnlyList<PluginInfo> GetEnabled() =>
        _plugins.Values.Where(p => p.IsEnabled).ToList().AsReadOnly();

    public PluginInfo? GetByName(string name) =>
        _plugins.GetValueOrDefault(name);

    public async Task EnableAsync(string name, CancellationToken ct)
    {
        if (!_plugins.TryGetValue(name, out PluginInfo? plugin))
            throw new InvalidOperationException($"Plugin '{name}' not found.");

        _plugins[name] = plugin with { IsEnabled = true };
        await UpdateRegistryEntryAsync(name, entry => entry with { Enabled = true });
        await RegisterPluginComponentsAsync(plugin);
        _logger.LogInformation("Plugin '{Name}' enabled", name);
    }

    public async Task DisableAsync(string name, CancellationToken ct)
    {
        if (!_plugins.TryGetValue(name, out PluginInfo? plugin))
            throw new InvalidOperationException($"Plugin '{name}' not found.");

        await UnregisterPluginComponentsAsync(plugin);
        _plugins[name] = plugin with { IsEnabled = false };
        await UpdateRegistryEntryAsync(name, entry => entry with { Enabled = false });
        _logger.LogInformation("Plugin '{Name}' disabled", name);
    }

    public async Task<PluginInfo> InstallAsync(PluginSource source, CancellationToken ct)
    {
        string pluginDir;

        if (source.Type == "git")
        {
            if (string.IsNullOrWhiteSpace(source.Url))
                throw new ArgumentException("Git source requires a URL.");

            // Derive plugin name from URL (last path segment without .git)
            string repoName = GitHelper.ExtractRepoName(source.Url);
            pluginDir = Path.Combine(_pluginsDir, repoName);

            if (Directory.Exists(pluginDir))
                throw new InvalidOperationException($"Plugin directory '{repoName}' already exists. Use update instead.");

            await GitHelper.CloneAsync(source.Url, source.Ref, pluginDir, ct);
        }
        else
        {
            throw new ArgumentException($"Unsupported source type: {source.Type}. Use 'git' or place folders directly for 'local'.");
        }

        // Load the plugin
        PluginInfo plugin = LoadPlugin(pluginDir, source);

        // Register in the registry
        var entry = new PluginRegistryEntry
        {
            Enabled = true,
            Source = source,
            InstalledAt = DateTimeOffset.UtcNow
        };
        await AddRegistryEntryAsync(plugin.Name, entry);
        _plugins[plugin.Name] = plugin;

        await RegisterPluginComponentsAsync(plugin);

        _logger.LogInformation("Plugin '{Name}' installed from {Source}", plugin.Name, source.Type);
        return plugin;
    }

    public async Task<PluginInfo> InstallFromDirectoryAsync(string clonedDir, PluginSource source, CancellationToken ct)
    {
        if (!Directory.Exists(clonedDir))
            throw new ArgumentException($"Directory not found: {clonedDir}");

        // Derive target name from directory
        string dirName = Path.GetFileName(clonedDir);
        string pluginDir = Path.Combine(_pluginsDir, dirName);

        if (Directory.Exists(pluginDir))
            throw new InvalidOperationException($"Plugin directory '{dirName}' already exists. Use update instead.");

        Directory.CreateDirectory(_pluginsDir);
        Directory.Move(clonedDir, pluginDir);

        // Load the plugin
        PluginInfo plugin = LoadPlugin(pluginDir, source);

        // Register in the registry
        var entry = new PluginRegistryEntry
        {
            Enabled = true,
            Source = source,
            InstalledAt = DateTimeOffset.UtcNow
        };
        await AddRegistryEntryAsync(plugin.Name, entry);
        _plugins[plugin.Name] = plugin;

        await RegisterPluginComponentsAsync(plugin);

        _logger.LogInformation("Plugin '{Name}' installed from pre-cloned directory", plugin.Name);
        return plugin;
    }

    public async Task UninstallAsync(string name, CancellationToken ct)
    {
        if (!_plugins.TryRemove(name, out PluginInfo? plugin))
            throw new InvalidOperationException($"Plugin '{name}' not found.");

        // Unregister plugin components before deleting files
        await UnregisterPluginComponentsAsync(plugin);

        // Remove from registry
        await RemoveRegistryEntryAsync(name);

        // Delete plugin directory
        if (Directory.Exists(plugin.RootPath))
        {
            try
            {
                ClearReadOnlyAttributes(plugin.RootPath);
                Directory.Delete(plugin.RootPath, recursive: true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete plugin directory: {Path}", plugin.RootPath);
            }
        }

        _logger.LogInformation("Plugin '{Name}' uninstalled", name);
    }

    public async Task<PluginInfo> UpdateAsync(string name, CancellationToken ct)
    {
        if (!_plugins.TryGetValue(name, out PluginInfo? plugin))
            throw new InvalidOperationException($"Plugin '{name}' not found.");

        if (plugin.Source.Type != "git")
            throw new InvalidOperationException($"Plugin '{name}' is not a git-sourced plugin.");

        await GitHelper.PullAsync(plugin.RootPath, ct);

        // Unregister old components, reload, re-register
        await UnregisterPluginComponentsAsync(plugin);

        // Reload the plugin
        PluginInfo updated = LoadPlugin(plugin.RootPath, plugin.Source) with { IsEnabled = plugin.IsEnabled };
        _plugins[name] = updated;

        if (updated.IsEnabled)
            await RegisterPluginComponentsAsync(updated);

        _logger.LogInformation("Plugin '{Name}' updated", name);
        return updated;
    }

    public async Task ReloadAsync(CancellationToken ct)
    {
        _plugins.Clear();
        PluginRegistryFile registry = await LoadRegistryAsync();

        // Discover subdirectories in plugins dir
        if (!Directory.Exists(_pluginsDir)) return;

        foreach (string dir in Directory.GetDirectories(_pluginsDir))
        {
            string dirName = Path.GetFileName(dir);
            if (dirName.StartsWith('.')) continue; // Skip hidden dirs

            try
            {
                PluginSource source = registry.Plugins.TryGetValue(dirName, out PluginRegistryEntry? entry)
                    ? entry.Source
                    : PluginSource.Local;

                bool enabled = !registry.Plugins.TryGetValue(dirName, out PluginRegistryEntry? regEntry) || regEntry.Enabled;

                PluginInfo plugin = LoadPlugin(dir, source) with { IsEnabled = enabled };

                if (regEntry is not null)
                    plugin = plugin with { InstalledAt = regEntry.InstalledAt };

                _plugins[plugin.Name] = plugin;

                // Auto-register discovered plugins not yet in registry
                if (!registry.Plugins.ContainsKey(dirName))
                {
                    await AddRegistryEntryAsync(plugin.Name, new PluginRegistryEntry
                    {
                        Enabled = true,
                        Source = source,
                        InstalledAt = DateTimeOffset.UtcNow
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load plugin from directory: {Dir}", dir);
            }
        }

        // Register components for all enabled plugins after discovery
        foreach (PluginInfo loadedPlugin in _plugins.Values.Where(p => p.IsEnabled))
        {
            try
            {
                await RegisterPluginComponentsAsync(loadedPlugin);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register components for plugin: {Name}", loadedPlugin.Name);
            }
        }
    }

    // ── Plugin Component Registration ──────────────────────────────────────

    private async Task RegisterPluginComponentsAsync(PluginInfo plugin)
    {
        // Skills: register each skill path's parent directory as a skill root
        if (_skillRegistrar is not null && plugin.SkillPaths.Count > 0)
        {
            var roots = plugin.SkillPaths
                .Select(p => Path.GetDirectoryName(p)!)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots)
            {
                _skillRegistrar.AddRoot(root);
                _logger.LogDebug("Registered skill root '{Root}' from plugin '{Name}'", root, plugin.Name);
            }
        }

        // MCP Servers: load from .mcp.json
        if (_mcpRegistrar is not null && plugin.McpConfigPath is not null)
        {
            await _mcpRegistrar.RegisterFromConfigFileAsync(plugin.McpConfigPath, plugin.Name);
        }

        // Agents: import from markdown files
        if (_agentRegistrar is not null && plugin.AgentPaths.Count > 0)
        {
            foreach (string agentPath in plugin.AgentPaths)
            {
                await _agentRegistrar.ImportFromFileAsync(agentPath, plugin.Name);
                _logger.LogDebug("Imported agent from '{Path}' for plugin '{Name}'", agentPath, plugin.Name);
            }
        }
    }

    private async Task UnregisterPluginComponentsAsync(PluginInfo plugin)
    {
        // Skills: remove registered roots
        if (_skillRegistrar is not null && plugin.SkillPaths.Count > 0)
        {
            var roots = plugin.SkillPaths
                .Select(p => Path.GetDirectoryName(p)!)
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (string root in roots)
                _skillRegistrar.RemoveRoot(root);
        }

        // MCP Servers: unregister by plugin name
        _mcpRegistrar?.UnregisterByPlugin(plugin.Name);

        // Agents: remove by plugin name
        if (_agentRegistrar is not null)
            await _agentRegistrar.RemoveByPluginAsync(plugin.Name);
    }

    // ── Plugin Loading ──────────────────────────────────────────────────────

    private PluginInfo LoadPlugin(string pluginDir, PluginSource source)
    {
        PluginManifest? manifest = LoadManifest(pluginDir);
        string name = manifest?.Name ?? Path.GetFileName(pluginDir);

        // Resolve skill paths
        string skillsDir = ResolveComponentPath(pluginDir, manifest?.Skills, "skills");
        var skillPaths = new List<string>();
        if (Directory.Exists(skillsDir))
        {
            foreach (string skillDir in Directory.GetDirectories(skillsDir))
            {
                if (File.Exists(Path.Combine(skillDir, "SKILL.md")))
                    skillPaths.Add(skillDir);
            }
        }

        // Resolve agent paths
        string agentsDir = ResolveComponentPath(pluginDir, manifest?.Agents, "agents");
        var agentPaths = new List<string>();
        if (Directory.Exists(agentsDir))
        {
            foreach (string file in Directory.GetFiles(agentsDir, "*.md"))
                agentPaths.Add(file);
        }

        // Load hooks
        var hooks = LoadHooks(pluginDir, manifest, name);

        // MCP config path
        string? mcpConfigPath = ResolveMcpConfigPath(pluginDir, manifest);

        return new PluginInfo
        {
            Name = name,
            RootPath = Path.GetFullPath(pluginDir),
            IsEnabled = true,
            Manifest = manifest,
            Source = source,
            InstalledAt = DateTimeOffset.UtcNow,
            SkillPaths = skillPaths.AsReadOnly(),
            AgentPaths = agentPaths.AsReadOnly(),
            Hooks = hooks.AsReadOnly(),
            McpConfigPath = mcpConfigPath
        };
    }

    private static PluginManifest? LoadManifest(string pluginDir)
    {
        // Claude format: .claude-plugin/plugin.json
        string claudeManifest = Path.Combine(pluginDir, ".claude-plugin", "plugin.json");
        if (File.Exists(claudeManifest))
        {
            string json = File.ReadAllText(claudeManifest);
            return JsonSerializer.Deserialize<PluginManifest>(json, JsonOpts);
        }

        // Fallback: plugin.json at root
        string rootManifest = Path.Combine(pluginDir, "plugin.json");
        if (File.Exists(rootManifest))
        {
            string json = File.ReadAllText(rootManifest);
            return JsonSerializer.Deserialize<PluginManifest>(json, JsonOpts);
        }

        return null;
    }

    private static List<HookConfig> LoadHooks(string pluginDir, PluginManifest? manifest, string pluginName)
    {
        string hooksPath = !string.IsNullOrWhiteSpace(manifest?.Hooks)
            ? Path.GetFullPath(Path.Combine(pluginDir, manifest.Hooks.TrimStart('.', '/')))
            : Path.Combine(pluginDir, "hooks", "hooks.json");

        if (!File.Exists(hooksPath))
            return [];

        try
        {
            string json = File.ReadAllText(hooksPath);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("hooks", out JsonElement hooksObj))
                return [];

            var hooks = new List<HookConfig>();
            string pluginRoot = Path.GetFullPath(pluginDir);

            foreach (JsonProperty eventProp in hooksObj.EnumerateObject())
            {
                if (!Enum.TryParse<HookEvent>(eventProp.Name, ignoreCase: false, out HookEvent hookEvent))
                    continue;

                foreach (JsonElement item in eventProp.Value.EnumerateArray())
                {
                    // Two formats: flat (type+command directly) or matcher (matcher + hooks array)
                    if (item.TryGetProperty("matcher", out JsonElement matcherEl))
                    {
                        string? matcher = matcherEl.GetString();
                        if (item.TryGetProperty("hooks", out JsonElement innerHooks))
                        {
                            foreach (JsonElement inner in innerHooks.EnumerateArray())
                            {
                                HookConfig? h = ParseHookEntry(inner, hookEvent, matcher, pluginName, pluginRoot);
                                if (h is not null) hooks.Add(h);
                            }
                        }
                    }
                    else
                    {
                        HookConfig? h = ParseHookEntry(item, hookEvent, null, pluginName, pluginRoot);
                        if (h is not null) hooks.Add(h);
                    }
                }
            }

            return hooks;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static HookConfig? ParseHookEntry(JsonElement el, HookEvent hookEvent, string? matcher, string pluginName, string pluginRoot)
    {
        string? type = el.TryGetProperty("type", out JsonElement typeEl) ? typeEl.GetString() : null;
        if (type is null) return null;

        string? command = el.TryGetProperty("command", out JsonElement cmdEl) ? cmdEl.GetString() : null;
        string? url = el.TryGetProperty("url", out JsonElement urlEl) ? urlEl.GetString() : null;

        // Expand plugin root variable
        command = ExpandPluginRoot(command, pluginRoot);
        url = ExpandPluginRoot(url, pluginRoot);

        return new HookConfig
        {
            Event = hookEvent,
            Matcher = matcher,
            Type = type,
            Command = command,
            Url = url,
            PluginName = pluginName,
            PluginRoot = pluginRoot
        };
    }

    private static string? ExpandPluginRoot(string? value, string pluginRoot)
    {
        if (value is null) return null;
        return value
            .Replace("${MICROCLAW_PLUGIN_ROOT}", pluginRoot)
            .Replace("${CLAUDE_PLUGIN_ROOT}", pluginRoot);
    }

    private static string ResolveComponentPath(string pluginDir, string? manifestPath, string defaultDir)
    {
        if (!string.IsNullOrWhiteSpace(manifestPath))
            return Path.GetFullPath(Path.Combine(pluginDir, manifestPath.TrimStart('.', '/')));
        return Path.Combine(pluginDir, defaultDir);
    }

    private static string? ResolveMcpConfigPath(string pluginDir, PluginManifest? manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest?.McpServers))
        {
            string path = Path.GetFullPath(Path.Combine(pluginDir, manifest.McpServers.TrimStart('.', '/')));
            return File.Exists(path) ? path : null;
        }

        string defaultPath = Path.Combine(pluginDir, ".mcp.json");
        return File.Exists(defaultPath) ? defaultPath : null;
    }

    // ── Registry File Operations ────────────────────────────────────────────

    private async Task<PluginRegistryFile> LoadRegistryAsync()
    {
        if (!File.Exists(_registryPath))
            return new PluginRegistryFile();

        try
        {
            string json = await File.ReadAllTextAsync(_registryPath);
            return JsonSerializer.Deserialize<PluginRegistryFile>(json, JsonOpts) ?? new PluginRegistryFile();
        }
        catch
        {
            return new PluginRegistryFile();
        }
    }

    private async Task SaveRegistryAsync(PluginRegistryFile registry)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_registryPath)!);
        string json = JsonSerializer.Serialize(registry, JsonOpts);
        await File.WriteAllTextAsync(_registryPath, json);
    }

    private async Task AddRegistryEntryAsync(string name, PluginRegistryEntry entry)
    {
        PluginRegistryFile registry = await LoadRegistryAsync();
        registry.Plugins[name] = entry;
        await SaveRegistryAsync(registry);
    }

    private async Task RemoveRegistryEntryAsync(string name)
    {
        PluginRegistryFile registry = await LoadRegistryAsync();
        registry.Plugins.Remove(name);
        await SaveRegistryAsync(registry);
    }

    private async Task UpdateRegistryEntryAsync(string name, Func<PluginRegistryEntry, PluginRegistryEntry> updater)
    {
        PluginRegistryFile registry = await LoadRegistryAsync();
        if (registry.Plugins.TryGetValue(name, out PluginRegistryEntry? existing))
        {
            registry.Plugins[name] = updater(existing);
            await SaveRegistryAsync(registry);
        }
    }

    /// <summary>
    /// Recursively clears read-only attributes so <see cref="Directory.Delete(string,bool)"/> can remove git pack files etc.
    /// </summary>
    private static void ClearReadOnlyAttributes(string directory)
    {
        foreach (string file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
        {
            FileAttributes attrs = File.GetAttributes(file);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
        }
    }
}
