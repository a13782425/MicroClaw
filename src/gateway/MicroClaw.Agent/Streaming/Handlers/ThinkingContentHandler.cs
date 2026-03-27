using MicroClaw.Gateway.Contracts.Streaming;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent.Streaming.Handlers;

/// <summary>处理 MEAI <see cref="TextReasoningContent"/>（模型思考/推理内容），转换为 <see cref="ThinkingItem"/>。</summary>
public sealed class ThinkingContentHandler : IAIContentHandler
{
    public bool CanHandle(AIContent content) => content is TextReasoningContent;

    public StreamItem? Convert(AIContent content)
    {
        var tc = (TextReasoningContent)content;
        return string.IsNullOrEmpty(tc.Text) ? null : new ThinkingItem(tc.Text);
    }
}
