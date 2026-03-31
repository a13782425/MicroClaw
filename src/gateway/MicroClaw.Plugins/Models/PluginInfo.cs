using MicroClaw.Plugins.Hooks;

namespace MicroClaw.Plugins.Models;

/// <summary>
/// Runtime information about a loaded plugin.
/// </summary>
public sealed record PluginInfo
{
    /// <summary>Plugin name (from manifest or directory name).</summary>
    public required string Name { get; init; }

    /// <summary>Absolute path to the plugin root directory.</summary>
    public required string RootPath { get; init; }

    /// <summary>Whether the plugin is currently enabled.</summary>
    public required bool IsEnabled { get; init; }

    /// <summary>Parsed manifest, or null for manifest-less plugins.</summary>
    public PluginManifest? Manifest { get; init; }

    /// <summary>Installation source metadata.</summary>
    public required PluginSource Source { get; init; }

    /// <summary>When the plugin was installed.</summary>
    public DateTimeOffset InstalledAt { get; init; }

    /// <summary>Discovered skill directory paths (absolute).</summary>
    public IReadOnlyList<string> SkillPaths { get; init; } = [];

    /// <summary>Discovered agent file paths (absolute).</summary>
    public IReadOnlyList<string> AgentPaths { get; init; } = [];

    /// <summary>Loaded hook configurations.</summary>
    public IReadOnlyList<HookConfig> Hooks { get; init; } = [];

    /// <summary>MCP server config file path, if present.</summary>
    public string? McpConfigPath { get; init; }
}
