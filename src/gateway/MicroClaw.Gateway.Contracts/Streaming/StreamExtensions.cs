using System.Text;

namespace MicroClaw.Gateway.Contracts.Streaming;

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
        List<ResponseAttachment> attachments = [];

        await foreach (StreamItem item in stream.WithCancellation(ct))
        {
            switch (item)
            {
                case TokenItem token:
                    text.Append(token.Content);
                    break;

                case DataContentItem data:
                    attachments.Add(new ResponseAttachment(data.MimeType, data.Data));
                    break;

                // ToolCallItem, ToolResultItem, SubAgent* items are ignored during materialization
            }
        }

        (string think, string main) = ThinkContentParser.Extract(text.ToString());

        return new AgentResponse(
            main,
            string.IsNullOrWhiteSpace(think) ? null : think,
            attachments);
    }
}
