using System.Text.Json;
using MicroClaw.Abstractions.Sessions;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent.Restorers;

/// <summary>MessageType == "tool_call" → FunctionCallContent。</summary>
public sealed class FunctionCallRestorer : IChatContentRestorer
{
    public bool CanRestore(SessionMessage message) =>
        message.MessageType == "tool_call" && message.Metadata is not null;

    public IEnumerable<AIContent> Restore(SessionMessage message)
    {
        var meta = message.Metadata!;
        string? callId = meta.TryGetValue("callId", out var cidEl) ? cidEl.GetString() : null;
        string? toolName = meta.TryGetValue("toolName", out var tnEl) ? tnEl.GetString() : null;

        if (callId is null || toolName is null) yield break;

        IDictionary<string, object?>? args = meta.TryGetValue("arguments", out var argsEl)
            && argsEl.ValueKind == JsonValueKind.Object
            ? argsEl.Deserialize<Dictionary<string, object?>>()
            : null;

        yield return new FunctionCallContent(callId, toolName, args);
    }
}
