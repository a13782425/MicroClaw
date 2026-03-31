namespace MicroClaw.Plugins.Models;

/// <summary>
/// A single plugin entry listed in a marketplace index.
/// </summary>
public sealed record MarketplacePluginEntry
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public PluginAuthor? Author { get; init; }
    public string? Homepage { get; init; }
    public IReadOnlyList<string>? Keywords { get; init; }
    public string? Version { get; init; }
    public required MarketplacePluginSource Source { get; init; }
    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary>
/// Describes where a marketplace plugin's source code lives.
/// Supports multiple source types used by different marketplaces.
/// </summary>
public sealed record MarketplacePluginSource
{
    public required MarketplacePluginSourceType SourceType { get; init; }

    /// <summary>Git URL for Url/GitSubdir types, or GitHub "owner/repo" shorthand.</summary>
    public string? Url { get; init; }

    /// <summary>Relative path within the marketplace repo (Local) or within a remote repo (GitSubdir).</summary>
    public string? Path { get; init; }

    /// <summary>Git branch/tag reference.</summary>
    public string? Ref { get; init; }

    /// <summary>Commit SHA for pinning.</summary>
    public string? Sha { get; init; }

    /// <summary>GitHub "owner/repo" shorthand (Copilot format).</summary>
    public string? Repo { get; init; }
}

public enum MarketplacePluginSourceType
{
    /// <summary>Plugin lives inside the marketplace repository at a relative path.</summary>
    Local,

    /// <summary>Plugin is a standalone Git repository.</summary>
    Url,

    /// <summary>Plugin is a subdirectory within an external Git repository.</summary>
    GitSubdir,

    /// <summary>Plugin references a GitHub repository (Copilot format).</summary>
    GitHub
}
