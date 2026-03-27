using MicroClaw.Gateway.Contracts.Streaming;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent.Streaming.Handlers;

/// <summary>处理 <see cref="TextContent"/>，转换为 <see cref="TokenItem"/>。</summary>
public sealed class TextContentHandler : IAIContentHandler
{
    public bool CanHandle(AIContent content) => content is TextContent;

    public StreamItem? Convert(AIContent content)
    {
        var text = (TextContent)content;
        return string.IsNullOrEmpty(text.Text) ? null : new TokenItem(text.Text);
    }
}
