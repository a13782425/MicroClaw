using System.Text.Json.Serialization;

namespace MicroClaw.Plugins.Models;

/// <summary>
/// Describes how a plugin was installed.
/// </summary>
public sealed record PluginSource
{
    [JsonPropertyName("type")]
    public required string Type { get; init; } // "local", "git"

    [JsonPropertyName("url")]
    public string? Url { get; init; }

    [JsonPropertyName("ref")]
    public string? Ref { get; init; }

    public static PluginSource Local => new() { Type = "local" };

    public static PluginSource Git(string url, string? gitRef = null) => new()
    {
        Type = "git",
        Url = url,
        Ref = gitRef
    };
}
