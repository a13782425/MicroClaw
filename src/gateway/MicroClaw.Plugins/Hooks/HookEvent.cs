namespace MicroClaw.Plugins.Hooks;

/// <summary>
/// Agent lifecycle events that hooks can subscribe to.
/// Aligned with Claude Code hook event names.
/// </summary>
public enum HookEvent
{
    SessionStart,
    PreToolUse,
    PostToolUse,
    PostToolUseFailure,
    Stop,
    OnError,
    SessionEnd
}
