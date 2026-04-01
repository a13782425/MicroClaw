using MicroClaw.Abstractions.Streaming;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent.Streaming;

/// <summary>
/// 将 MEAI <see cref="AIContent"/> 转换为 <see cref="StreamItem"/> 的处理器接口。
/// 每种 AIContent 类型对应一个 Handler 实现，通过 <see cref="AIContentPipeline"/> 自动聚合。
/// </summary>
public interface IAIContentHandler
{
    /// <summary>判断是否能处理该内容类型。</summary>
    bool CanHandle(AIContent content);

    /// <summary>
    /// 将 AIContent 转为 StreamItem。返回 null 表示该内容仅用于内部处理（如 Usage 捕获），不产生流事件。
    /// </summary>
    StreamItem? Convert(AIContent content);
}
