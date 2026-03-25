namespace MicroClaw.Infrastructure.Data;

/// <summary>F-D-1: 渠道消息失败重试队列实体（SQLite 持久化）。</summary>
public sealed class ChannelRetryQueueEntity
{
    public string Id { get; set; } = string.Empty;

    /// <summary>渠道类型，如 "feishu"。</summary>
    public string ChannelType { get; set; } = string.Empty;

    /// <summary>渠道配置 ID（对应 channels 表 id）。</summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>已创建的会话 ID（消息已写入会话历史）。</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>飞书原始消息 ID（用于回复原始消息线程）。</summary>
    public string MessageId { get; set; } = string.Empty;

    /// <summary>用户原始文本。</summary>
    public string UserText { get; set; } = string.Empty;

    /// <summary>已重试次数（0 = 尚未重试，来自初始失败）。</summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>状态：pending / exhausted。</summary>
    public string Status { get; set; } = "pending";

    /// <summary>下次允许重试的时间：相对于 TimeBase.BaseTime 的毫秒偏移。</summary>
    public long NextRetryAtMs { get; set; }

    /// <summary>入队时间：相对于 TimeBase.BaseTime 的毫秒偏移。</summary>
    public long CreatedAtMs { get; set; }

    /// <summary>最近一次失败的错误摘要。</summary>
    public string? LastErrorMessage { get; set; }
}
