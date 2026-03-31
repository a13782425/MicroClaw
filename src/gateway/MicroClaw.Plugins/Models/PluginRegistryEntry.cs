using System.Text.Json.Serialization;

namespace MicroClaw.Plugins.Models;

/// <summary>
/// Entry in the global plugin registry file (<c>workspace/plugins/plugin.json</c>).
/// </summary>
public sealed record PluginRegistryEntry
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    [JsonPropertyName("source")]
    public required PluginSource Source { get; init; }

    [JsonPropertyName("installedAt")]
    public DateTimeOffset InstalledAt { get; init; }
}

/// <summary>
/// Root object of the plugin registry file.
/// </summary>
public sealed class PluginRegistryFile
{
    [JsonPropertyName("plugins")]
    public Dictionary<string, PluginRegistryEntry> Plugins { get; init; } = new();
}
