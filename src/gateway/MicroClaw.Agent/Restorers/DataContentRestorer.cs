using MicroClaw.Abstractions.Sessions;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent.Restorers;

/// <summary>Attachments → DataContent × N。</summary>
public sealed class DataContentRestorer : IChatContentRestorer
{
    public bool CanRestore(SessionMessage message) => message.Attachments is { Count: > 0 };

    public IEnumerable<AIContent> Restore(SessionMessage message)
    {
        foreach (MessageAttachment att in message.Attachments!)
        {
            byte[] bytes = Convert.FromBase64String(att.Base64Data);
            yield return new DataContent(bytes, att.MimeType);
        }
    }
}
