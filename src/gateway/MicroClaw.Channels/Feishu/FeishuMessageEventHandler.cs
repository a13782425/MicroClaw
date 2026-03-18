using FeishuNetSdk;
using FeishuNetSdk.Im.Events;
using FeishuNetSdk.Services;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// 飞书 WebSocket 长连接事件处理器。
/// SDK 通过反射自动发现此类，在收到 im.message.receive_v1 事件时调用 ExecuteAsync。
/// 每个子 ServiceProvider 包含独立的 FeishuChannelContext，标识当前渠道。
/// </summary>
public sealed class FeishuMessageEventHandler(
    FeishuChannelContext channelContext,
    FeishuMessageProcessor processor,
    ILogger<FeishuMessageEventHandler> logger)
    : IEventHandler<EventV2Dto<ImMessageReceiveV1EventBodyDto>, ImMessageReceiveV1EventBodyDto>
{
    public Task ExecuteAsync(EventV2Dto<ImMessageReceiveV1EventBodyDto> input, CancellationToken cancellationToken)
    {
        ImMessageReceiveV1EventBodyDto? body = input.Event;
        if (body is null) return Task.CompletedTask;

        string? userText = FeishuMessageProcessor.ExtractText(body);
        if (string.IsNullOrWhiteSpace(userText)) return Task.CompletedTask;

        string? messageId = body.Message?.MessageId;
        string? chatId = body.Message?.ChatId;
        if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(chatId))
            return Task.CompletedTask;

        string? senderId = body.Sender?.SenderId?.OpenId;
        ChannelConfig channel = channelContext.Channel;
        FeishuChannelSettings settings = channelContext.Settings;

        logger.LogInformation("飞书 WebSocket 消息 channel={ChannelId} from={SenderId}: {Text}",
            channel.Id, senderId, userText);

        // fire-and-forget：SDK 要求 3 秒内返回，AI 调用可能耗时较长
        // 不传入 tenantApi，因为 scoped 容器会在本方法返回后释放；
        // ProcessMessageAsync 内部会自建 ServiceProvider 来回复消息。
        _ = Task.Run(() => processor.ProcessMessageAsync(
            userText, senderId, chatId, messageId, channel, settings, tenantApi: null, CancellationToken.None));

        return Task.CompletedTask;
    }
}
