using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;

namespace MicroClaw.Streaming.PersistenceHandlers;

/// <summary>SubAgentStartItem → SessionMessage（role=system, messageType=sub_agent_start）。</summary>
public sealed class SubAgentStartPersistenceHandler : IStreamItemPersistenceHandler
{
    public bool CanHandle(StreamItem item) => item is SubAgentStartItem;

    public SessionMessage? ToPersistenceMessage(StreamItem item)
    {
        var s = (SubAgentStartItem)item;
        return new SessionMessage(
            Id: item.MessageId ?? Guid.NewGuid().ToString("N"),
            Role: "system",
            Content: $"子代理 {s.AgentName} 开始执行",
            ThinkContent: null,
            Timestamp: DateTimeOffset.UtcNow,
            Attachments: null,
            MessageType: "sub_agent_start",
            Metadata: MetadataHelper.ToJsonElements(new Dictionary<string, object?>
            {
                ["agentId"] = s.AgentId,
                ["agentName"] = s.AgentName,
                ["task"] = s.Task,
                ["childSessionId"] = s.ChildSessionId
            }));
    }
}
