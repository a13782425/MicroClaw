using MicroClaw.Abstractions.Sessions;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent.Restorers;

/// <summary>ThinkContent → TextReasoningContent。</summary>
public sealed class ThinkingContentRestorer : IChatContentRestorer
{
    public bool CanRestore(SessionMessage message) => !string.IsNullOrEmpty(message.ThinkContent);

    public IEnumerable<AIContent> Restore(SessionMessage message)
    {
        yield return new TextReasoningContent(message.ThinkContent!);
    }
}
