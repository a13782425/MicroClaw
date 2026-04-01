using MicroClaw.Abstractions.Sessions;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent.Restorers;

/// <summary>MessageType == "tool_result" → FunctionResultContent。</summary>
public sealed class FunctionResultRestorer : IChatContentRestorer
{
    public bool CanRestore(SessionMessage message) =>
        message.MessageType == "tool_result" && message.Metadata is not null;

    public IEnumerable<AIContent> Restore(SessionMessage message)
    {
        string? callId = message.Metadata!.TryGetValue("callId", out var cidEl) ? cidEl.GetString() : null;
        if (callId is null) yield break;

        yield return new FunctionResultContent(callId, message.Content);
    }
}
