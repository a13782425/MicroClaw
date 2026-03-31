using MicroClaw.Plugins.Models;

namespace MicroClaw.Plugins;

/// <summary>
/// Plugin registry — manages plugin lifecycle (load, enable, disable, install, uninstall).
/// </summary>
public interface IPluginRegistry
{
    /// <summary>Get all known plugins.</summary>
    IReadOnlyList<PluginInfo> GetAll();

    /// <summary>Get all enabled plugins.</summary>
    IReadOnlyList<PluginInfo> GetEnabled();

    /// <summary>Get a plugin by name.</summary>
    PluginInfo? GetByName(string name);

    /// <summary>Enable a plugin.</summary>
    Task EnableAsync(string name, CancellationToken ct = default);

    /// <summary>Disable a plugin.</summary>
    Task DisableAsync(string name, CancellationToken ct = default);

    /// <summary>Install a plugin from a source.</summary>
    Task<PluginInfo> InstallAsync(PluginSource source, CancellationToken ct = default);

    /// <summary>Install a plugin from an already-cloned directory (moves it into the plugins folder).</summary>
    Task<PluginInfo> InstallFromDirectoryAsync(string clonedDir, PluginSource source, CancellationToken ct = default);

    /// <summary>Uninstall a plugin.</summary>
    Task UninstallAsync(string name, CancellationToken ct = default);

    /// <summary>Update a git-sourced plugin.</summary>
    Task<PluginInfo> UpdateAsync(string name, CancellationToken ct = default);

    /// <summary>Reload all plugins from disk.</summary>
    Task ReloadAsync(CancellationToken ct = default);
}
