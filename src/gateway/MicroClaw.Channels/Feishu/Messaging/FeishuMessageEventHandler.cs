using FeishuNetSdk;
using FeishuNetSdk.Im.Events;
using FeishuNetSdk.Services;
using MicroClaw.Channels;
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

        // F-A-7: 先构建 Key→Name mention 字典，再提取文本时传入以保留被提及用户名称
        IReadOnlyDictionary<string, string>? mentionMap = null;
        if (body.Message?.Mentions is { Length: > 0 })
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var m in body.Message.Mentions)
            {
                if (string.IsNullOrEmpty(m.Key)) continue;
                string displayName = !string.IsNullOrWhiteSpace(m.Name) ? m.Name
                    : m.Id?.OpenId ?? m.Id?.UserId ?? m.Id?.UnionId ?? string.Empty;
                if (!string.IsNullOrEmpty(displayName))
                    map[m.Key] = displayName;
            }
            if (map.Count > 0) mentionMap = map;
        }

        string? userText = FeishuMessageProcessor.ExtractText(body, mentionMap);
        if (string.IsNullOrWhiteSpace(userText)) return Task.CompletedTask;

        string? messageId = body.Message?.MessageId;
        string? chatId = body.Message?.ChatId;
        if (string.IsNullOrWhiteSpace(messageId) || string.IsNullOrWhiteSpace(chatId))
            return Task.CompletedTask;

        string? senderId = body.Sender?.SenderId?.OpenId;
        ChannelConfig channel = channelContext.Channel;
        FeishuChannelSettings settings = channelContext.Settings;

        // F-F-1: 全链路追踪 — WebSocket 接收步骤
        string traceId = messageId!.Length >= 8 ? messageId[..8] : messageId;
        logger.LogInformation(
            "[{TraceId}] WebSocket 接收 channel={ChannelId} from={SenderId} messageId={MessageId}: {Text}",
            traceId, channel.Id, senderId, messageId, userText);

        // F-B-1: 提取 chatType 和被 @ 的 open_id 列表
        string chatType = body.Message?.ChatType ?? "p2p";
        IReadOnlyList<string> mentionedOpenIds = body.Message?.Mentions is { Length: > 0 }
            ? body.Message.Mentions
                .Select(m => m.Id?.OpenId)
                .OfType<string>()
                .ToList()
            : [];

        // F-B-3: 提取 rootId，话题内消息 (root_id 非空) 回复时会带 reply_in_thread
        string? rootId = body.Message?.RootId;

        // fire-and-forget：SDK 要求 3 秒内返回，AI 调用可能耗时较长
        // 不传入 tenantApi，因为 scoped 容器会在本方法返回后释放；
        // ProcessMessageAsync 内部会自建 ServiceProvider 来回复消息。
        _ = Task.Run(() => processor.ProcessMessageAsync(
            userText, senderId, chatId, messageId, channel, settings,
            chatType, mentionedOpenIds, rootId: rootId, ct: CancellationToken.None));

        return Task.CompletedTask;
    }
}
