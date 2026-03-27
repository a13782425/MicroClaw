using MicroClaw.Gateway.Contracts.Streaming;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent.Streaming.Handlers;

/// <summary>处理 <see cref="DataContent"/>（图片/音频等），转换为 <see cref="DataContentItem"/>。</summary>
public sealed class DataContentHandler : IAIContentHandler
{
    public bool CanHandle(AIContent content) => content is DataContent;

    public StreamItem? Convert(AIContent content)
    {
        var dc = (DataContent)content;
        return dc.Data is { Length: > 0 }
            ? new DataContentItem(dc.MediaType ?? "application/octet-stream", dc.Data.ToArray())
            : null;
    }
}
