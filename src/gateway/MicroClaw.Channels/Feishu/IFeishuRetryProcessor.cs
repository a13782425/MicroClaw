using MicroClaw.Configuration.Options;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// 飞书消息重试回复接口，供 ChannelRetryJob 调用，无需引用内部 FeishuMessageProcessor。
/// </summary>
public interface IFeishuRetryProcessor
{
    /// <summary>重试时回复指定消息。</summary>
    Task SendRetryReplyAsync(string messageId, string text, FeishuChannelSettings settings,
        CancellationToken ct = default);
}
