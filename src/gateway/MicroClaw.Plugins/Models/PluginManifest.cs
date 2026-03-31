using System.Text.Json.Serialization;

namespace MicroClaw.Plugins.Models;

/// <summary>
/// Plugin manifest parsed from <c>.claude-plugin/plugin.json</c>.
/// Only <see cref="Name"/> is required; all other fields are optional.
/// </summary>
public sealed record PluginManifest
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("version")]
    public string? Version { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("author")]
    public PluginAuthor? Author { get; init; }

    [JsonPropertyName("keywords")]
    public IReadOnlyList<string>? Keywords { get; init; }

    [JsonPropertyName("skills")]
    public string? Skills { get; init; }

    [JsonPropertyName("agents")]
    public string? Agents { get; init; }

    [JsonPropertyName("hooks")]
    public string? Hooks { get; init; }

    [JsonPropertyName("mcpServers")]
    public string? McpServers { get; init; }
}

public sealed record PluginAuthor
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("email")]
    public string? Email { get; init; }

    [JsonPropertyName("url")]
    public string? Url { get; init; }
}
