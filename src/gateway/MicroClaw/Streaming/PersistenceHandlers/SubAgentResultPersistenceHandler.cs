using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Gateway.Contracts.Streaming;

namespace MicroClaw.Streaming.PersistenceHandlers;

/// <summary>SubAgentResultItem → SessionMessage（role=system, messageType=sub_agent_result）。</summary>
public sealed class SubAgentResultPersistenceHandler : IStreamItemPersistenceHandler
{
    public bool CanHandle(StreamItem item) => item is SubAgentResultItem;

    public SessionMessage? ToPersistenceMessage(StreamItem item)
    {
        var r = (SubAgentResultItem)item;
        return new SessionMessage(
            Id: item.MessageId ?? Guid.NewGuid().ToString("N"),
            Role: "system",
            Content: r.Result,
            ThinkContent: null,
            Timestamp: DateTimeOffset.UtcNow,
            Attachments: null,
            MessageType: "sub_agent_result",
            Metadata: MetadataHelper.ToJsonElements(new Dictionary<string, object?>
            {
                ["agentId"] = r.AgentId,
                ["agentName"] = r.AgentName,
                ["durationMs"] = r.DurationMs
            }));
    }
}
