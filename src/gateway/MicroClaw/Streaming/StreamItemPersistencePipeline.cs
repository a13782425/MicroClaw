using System.Text;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Gateway.Contracts.Streaming;

namespace MicroClaw.Streaming;

/// <summary>
/// 有状态的 StreamItem 持久化管道。累积 TokenItem/ThinkingItem 文本和 DataContentItem 附件，
/// 将其他类型分发给 <see cref="IStreamItemPersistenceHandler"/> 立即转为 SessionMessage。
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

    public StreamItemPersistencePipeline(IEnumerable<IStreamItemPersistenceHandler> handlers)
    {
        _handlers = handlers.ToList().AsReadOnly();
    }

    /// <summary>
    /// 处理单个 StreamItem。聚合型（Token/Thinking/DataContent）累积到内部状态，
    /// 其他类型通过 handler 转为 SessionMessage 立即记录。
    /// </summary>
    public SessionMessage? ProcessItem(StreamItem item)
    {
        switch (item)
        {
            case TokenItem token:
                _fullContent.Append(token.Content);
                return null;

            case ThinkingItem thinking:
                _thinkContent.Append(thinking.Content);
                return null;

            case DataContentItem data:
                string base64 = Convert.ToBase64String(data.Data);
                _attachments.Add(new MessageAttachment("attachment", data.MimeType, base64));
                return null;

            default:
                foreach (IStreamItemPersistenceHandler handler in _handlers)
                {
                    if (!handler.CanHandle(item)) continue;
                    SessionMessage? msg = handler.ToPersistenceMessage(item);
                    if (msg is not null)
                        _immediateMessages.Add(msg);
                    return msg;
                }
                return null; // 未匹配的类型（如 Workflow 事件）不持久化
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
        // 优先使用 MEAI 原生 ThinkingContent；若无则从文本中提取 <think> 块
        string fullText = _fullContent.ToString();
        string thinkText = _thinkContent.ToString();

        string mainContent;
        string? thinkContent;

        if (!string.IsNullOrWhiteSpace(thinkText))
        {
            // 有原生 ThinkingContent，文本中的 <think> 标签也一并提取
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
            Role: "assistant",
            Content: mainContent,
            ThinkContent: thinkContent,
            Timestamp: DateTimeOffset.UtcNow,
            Attachments: _attachments.Count > 0 ? _attachments : null);
    }
}
