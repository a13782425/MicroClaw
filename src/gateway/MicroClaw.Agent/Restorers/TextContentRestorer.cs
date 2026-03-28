using MicroClaw.Gateway.Contracts.Sessions;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent.Restorers;

/// <summary>普通文本消息（MessageType 为 null）→ TextContent。</summary>
public sealed class TextContentRestorer : IChatContentRestorer
{
    public bool CanRestore(SessionMessage message) =>
        message.MessageType is null && !string.IsNullOrEmpty(message.Content);

    public IEnumerable<AIContent> Restore(SessionMessage message)
    {
        yield return new TextContent(message.Content);
    }
}
