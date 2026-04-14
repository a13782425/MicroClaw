using MicroClaw.Agent.Streaming.Handlers;
using MicroClaw.Abstractions.Streaming;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.Streaming;

/// <summary>
/// AIContent 转换管道：遍历内部注册的 <see cref="IAIContentHandler"/> 将
/// <see cref="AIContent"/> 转为 <see cref="StreamItem"/>。
/// Handler 实例由 Pipeline 内部构建，无需通过 DI 逐个注册。
/// </summary>
public sealed class AIContentPipeline
{
    private readonly IReadOnlyList<IAIContentHandler> _handlers;
    private readonly ILogger<AIContentPipeline> _logger;

    public AIContentPipeline(ILogger<AIContentPipeline> logger)
    {
        _handlers = new IAIContentHandler[]
        {
            new TextContentHandler(),
            new DataContentHandler(),
            new UsageContentHandler(),
            new ThinkingContentHandler(),
        };
        _logger = logger;
    }

    /// <summary>
    /// 处理 <see cref="AgentResponseUpdate"/> 中的所有内容，返回转换后的 StreamItem 序列。
    /// 未匹配到任何 Handler 的内容会被记录日志后跳过。
    /// </summary>
    public IEnumerable<StreamItem> Process(IList<AIContent> contents)
    {
        foreach (AIContent content in contents)
        {
            bool handled = false;
            foreach (IAIContentHandler handler in _handlers)
            {
                if (!handler.CanHandle(content)) continue;
                handled = true;

                StreamItem? item = handler.Convert(content);
                if (item is not null)
                    yield return item;
                break; // 每个内容只由第一个匹配的 handler 处理
            }

            if (!handled)
            {
                _logger.LogDebug("No handler found for AIContent type: {ContentType}", content.GetType().Name);
            }
        }
    }
}
