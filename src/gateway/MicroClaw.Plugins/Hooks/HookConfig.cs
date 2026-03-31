using System.Text.Json.Serialization;

namespace MicroClaw.Plugins.Hooks;

/// <summary>
/// A single hook entry from <c>hooks.json</c>.
/// </summary>
public sealed record HookConfig
{
    /// <summary>The lifecycle event this hook responds to.</summary>
    public required HookEvent Event { get; init; }

    /// <summary>Regex pattern to match tool names. Null matches all tools.</summary>
    public string? Matcher { get; init; }

    /// <summary>Hook type: "command" or "http".</summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>Shell command to execute (for type="command"). Supports ${MICROCLAW_PLUGIN_ROOT}.</summary>
    public string? Command { get; init; }

    /// <summary>URL to POST to (for type="http").</summary>
    public string? Url { get; init; }

    /// <summary>Name of the plugin that owns this hook.</summary>
    public string? PluginName { get; init; }

    /// <summary>Absolute path to the plugin root (for variable expansion).</summary>
    public string? PluginRoot { get; init; }
}

/// <summary>
/// Context passed to hook executors.
/// </summary>
public sealed record HookContext
{
    public required HookEvent Event { get; init; }
    public string? ToolName { get; init; }
    public IDictionary<string, object?>? ToolArguments { get; init; }
    public string? ToolResult { get; init; }
    public bool? ToolSuccess { get; init; }
    public string? SessionId { get; init; }
    public string? AgentId { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of executing a hook.
/// </summary>
public sealed record HookResult
{
    /// <summary>Whether to allow the operation to proceed (relevant for PreToolUse).</summary>
    public HookDecision Decision { get; init; } = HookDecision.Continue;

    /// <summary>Output from the hook execution.</summary>
    public string? Output { get; init; }

    /// <summary>Reason for denial (when Decision is Deny).</summary>
    public string? DenyReason { get; init; }

    public static readonly HookResult Continue = new();
    public static HookResult Deny(string reason) => new() { Decision = HookDecision.Deny, DenyReason = reason };
}

public enum HookDecision
{
    Continue,
    Deny
}
