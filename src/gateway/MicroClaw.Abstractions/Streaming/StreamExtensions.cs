using System.Text;

namespace MicroClaw.Abstractions.Streaming;

/// <summary>将 IAsyncEnumerable&lt;StreamItem&gt; 物化为 AgentResponse 的扩展方法。</summary>
public static class StreamExtensions
{
    /// <summary>
    /// 消费整个流，收集所有 token 拼接为文本、DataContentItem 转为附件、提取 &lt;think&gt; 块。
    /// 适用于不需要逐项处理流的调用方（渠道回复、子代理等）。
    /// </summary>
    public static async Task<AgentResponse> MaterializeAsync(
        this IAsyncEnumerable<StreamItem> stream,
        CancellationToken ct = default)
    {
        StringBuilder text = new();
        StringBuilder thinkText = new();
        List<ResponseAttachment> attachments = [];

        await foreach (StreamItem item in stream.WithCancellation(ct))
        {
            switch (item)
            {
                case TokenItem token:
                    text.Append(token.Content);
                    break;

                case ThinkingItem thinking:
                    thinkText.Append(thinking.Content);
                    break;

                case DataContentItem data:
                    attachments.Add(new ResponseAttachment(data.MimeType, data.Data));
                    break;

                // ToolCallItem, ToolResultItem, SubAgent*, Workflow* items are ignored during materialization
            }
        }

        // 合并 MEAI 原生 ThinkingContent 与文本中的 <think> 标签
        (string extractedThink, string main) = ThinkContentParser.Extract(text.ToString());
        string? think = thinkText.Length > 0
            ? (string.IsNullOrWhiteSpace(extractedThink) ? thinkText.ToString() : thinkText + "\n" + extractedThink)
            : (string.IsNullOrWhiteSpace(extractedThink) ? null : extractedThink);

        return new AgentResponse(
            main,
            think,
            attachments);
    }
}
