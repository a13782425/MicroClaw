namespace MicroClaw.Gateway.Contracts;

/// <summary>
/// F-D-1: 渠道消息失败重试队列抽象。
/// 实现类位于主项目，注入到渠道消息处理器，AI 调用失败时将消息写入持久化队列。
/// </summary>
public interface IChannelRetryQueue
{
    /// <summary>将失败的渠道消息加入重试队列（幂等：同一 messageId 不重复入队）。</summary>
    Task EnqueueAsync(
        string channelType,
        string channelId,
        string sessionId,
        string messageId,
        string userText,
        string errorMessage,
        CancellationToken ct = default);
}
