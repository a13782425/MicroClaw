using System.Text;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Gateway.Contracts.Streaming;

namespace MicroClaw.Streaming;

/// <summary>
/// 有状态的 StreamItem 持久化管道。累积 TokenItem/ThinkingItem 文本和 DataContentItem 附件，
/// 将其他类型分发给 <see cref="IStreamItemPersistenceHandler"/> 立即转为 SessionMessage。
/// <para>
/// 当 ToolCallItem 或 SubAgentStartItem 到来时，先将已累积的文本 flush 为 assistant 消息（同一 messageId），
/// 再输出工具/子代理消息，保证文本顺序正确。
/// </para>
/// <para>
/// 生命周期应为 Scoped（per-request），因为内部持有聚合状态。
/// 调用方在流结束后调用 <see cref="Finalize"/> 获取最终聚合的 assistant 消息。
/// </para>
/// </summary>
public sealed class StreamItemPersistencePipeline
{
    private readonly IReadOnlyList<IStreamItemPersistenceHandler> _handlers;
    private readonly StringBuilder _fullContent = new();
    private readonly StringBuilder _thinkContent = new();
    private readonly List<MessageAttachment> _attachments = [];
    private readonly List<SessionMessage> _immediateMessages = [];
    private string? _currentMessageId;

    public StreamItemPersistencePipeline(IEnumerable<IStreamItemPersistenceHandler> handlers)
    {
        _handlers = handlers.ToList().AsReadOnly();
    }

    /// <summary>
    /// 处理单个 StreamItem。聚合型（Token/Thinking/DataContent）累积到内部状态，
    /// 其他类型通过 handler 转为 SessionMessage 立即记录。
    /// ToolCallItem/SubAgentStartItem 到来时先 flush 已累积文本。
    /// </summary>
    public IReadOnlyList<SessionMessage> ProcessItem(StreamItem item)
    {
        switch (item)
        {
            case TokenItem token:
                _currentMessageId = item.MessageId ?? _currentMessageId;
                _fullContent.Append(token.Content);
                return [];

            case ThinkingItem thinking:
                _currentMessageId = item.MessageId ?? _currentMessageId;
                _thinkContent.Append(thinking.Content);
                return [];

            case DataContentItem data:
                _currentMessageId = item.MessageId ?? _currentMessageId;
                string base64 = Convert.ToBase64String(data.Data);
                _attachments.Add(new MessageAttachment("attachment", data.MimeType, base64));
                return [];

            default:
            {
                var results = new List<SessionMessage>();

                // ToolCall/SubAgentStart 到来前先 flush 已累积的文本内容
                if (item is ToolCallItem or SubAgentStartItem)
                    FlushAccumulated(results);

                foreach (IStreamItemPersistenceHandler handler in _handlers)
                {
                    if (!handler.CanHandle(item)) continue;
                    SessionMessage? msg = handler.ToPersistenceMessage(item);
                    if (msg is not null)
                    {
                        _immediateMessages.Add(msg);
                        results.Add(msg);
                    }
                    break;
                }
                return results;
            }
        }
    }

    /// <summary>获取流处理过程中立即持久化的所有消息（不含最终聚合消息）。</summary>
    public IReadOnlyList<SessionMessage> ImmediateMessages => _immediateMessages;

    /// <summary>
    /// 流结束后调用，返回最终聚合的 assistant 消息（含完整文本、think 内容和附件）。
    /// 如果没有文本内容，返回 null。
    /// </summary>
    public SessionMessage? Finalize()
    {
        return BuildAccumulatedMessage();
    }

    /// <summary>将已累积的文本/think/附件 flush 为 assistant 消息并加入 results，然后清空累积状态。</summary>
    private void FlushAccumulated(List<SessionMessage> results)
    {
        SessionMessage? msg = BuildAccumulatedMessage();
        if (msg is not null)
            results.Add(msg);

        _fullContent.Clear();
        _thinkContent.Clear();
        _attachments.Clear();
    }

    /// <summary>从当前累积状态构建 assistant 消息（不清空状态）。</summary>
    private SessionMessage? BuildAccumulatedMessage()
    {
        // 优先使用 MEAI 原生 ThinkingContent；若无则从文本中提取 <think> 块
        string fullText = _fullContent.ToString();
        string thinkText = _thinkContent.ToString();

        string mainContent;
        string? thinkContent;

        if (!string.IsNullOrWhiteSpace(thinkText))
        {
            (string extractedThink, string extractedMain) = ThinkContentParser.Extract(fullText);
            mainContent = extractedMain;
            thinkContent = string.IsNullOrWhiteSpace(extractedThink)
                ? thinkText
                : thinkText + "\n" + extractedThink;
        }
        else
        {
            (string extractedThink, string extractedMain) = ThinkContentParser.Extract(fullText);
            mainContent = extractedMain;
            thinkContent = string.IsNullOrWhiteSpace(extractedThink) ? null : extractedThink;
        }

        if (string.IsNullOrWhiteSpace(mainContent))
            return null;

        return new SessionMessage(
            Id: _currentMessageId ?? Guid.NewGuid().ToString("N"),
            Role: "assistant",
            Content: mainContent,
            ThinkContent: thinkContent,
            Timestamp: DateTimeOffset.UtcNow,
            Attachments: _attachments.Count > 0 ? _attachments.ToList() : null);
    }
}
