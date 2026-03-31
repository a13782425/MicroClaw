namespace MicroClaw.Plugins.Hooks;

/// <summary>
/// Executes hooks registered by plugins for agent lifecycle events.
/// </summary>
public interface IHookExecutor
{
    /// <summary>
    /// Execute all matching hooks for the given event.
    /// For PreToolUse, returns the most restrictive decision (Deny overrides Continue).
    /// </summary>
    Task<HookResult> ExecuteAsync(HookContext context, CancellationToken ct = default);
}
