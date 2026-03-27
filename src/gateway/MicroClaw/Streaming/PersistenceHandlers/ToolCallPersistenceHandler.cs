using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Gateway.Contracts.Streaming;

namespace MicroClaw.Streaming.PersistenceHandlers;

/// <summary>ToolCallItem → SessionMessage（role=assistant, messageType=tool_call）。</summary>
public sealed class ToolCallPersistenceHandler : IStreamItemPersistenceHandler
{
    public bool CanHandle(StreamItem item) => item is ToolCallItem;

    public SessionMessage? ToPersistenceMessage(StreamItem item)
    {
        var tc = (ToolCallItem)item;
        return new SessionMessage(
            Role: "assistant",
            Content: $"调用工具: {tc.ToolName}",
            ThinkContent: null,
            Timestamp: DateTimeOffset.UtcNow,
            Attachments: null,
            MessageType: "tool_call",
            Metadata: MetadataHelper.ToJsonElements(new Dictionary<string, object?>
            {
                ["callId"] = tc.CallId,
                ["toolName"] = tc.ToolName,
                ["arguments"] = tc.Arguments
            }));
    }
}
