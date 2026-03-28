using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Gateway.Contracts.Streaming;

namespace MicroClaw.Streaming.PersistenceHandlers;

/// <summary>ToolResultItem → SessionMessage（role=tool, messageType=tool_result）。</summary>
public sealed class ToolResultPersistenceHandler : IStreamItemPersistenceHandler
{
    public bool CanHandle(StreamItem item) => item is ToolResultItem;

    public SessionMessage? ToPersistenceMessage(StreamItem item)
    {
        var tr = (ToolResultItem)item;
        string? visibility = item.Visibility;
        return new SessionMessage(
            Id: item.MessageId ?? Guid.NewGuid().ToString("N"),
            Role: "tool",
            Content: tr.Result,
            ThinkContent: null,
            Timestamp: DateTimeOffset.UtcNow,
            Attachments: null,
            MessageType: "tool_result",
            Metadata: MetadataHelper.ToJsonElements(new Dictionary<string, object?>
            {
                ["callId"] = tr.CallId,
                ["toolName"] = tr.ToolName,
                ["success"] = tr.Success,
                ["durationMs"] = tr.DurationMs
            }),
            Visibility: visibility);
    }
}
