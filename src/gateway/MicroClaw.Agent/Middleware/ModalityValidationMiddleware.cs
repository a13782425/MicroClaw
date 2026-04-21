using MicroClaw.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.Middleware;

/// <summary>
/// 模态校验中间件（逻辑从 <c>AgentRunner.ValidateModalities()</c> 提取为独立静态类）。
/// 根据 <see cref="ProviderConfig"/> 的能力声明，过滤掉当前 Provider 不支持的附件类型。
/// </summary>
public static class ModalityValidationMiddleware
{
    /// <summary>按 Provider 能力声明，从消息历史中移除不支持的附件类型。返回新列表，不修改原始集合。</summary>
    public static IReadOnlyList<ChatMessage> FilterUnsupportedModalities(
        IReadOnlyList<ChatMessage> messages,
        ProviderConfig provider,
        Microsoft.Extensions.Logging.ILogger logger)
    {
        // 快速路径：若无 DataContent 则无需检查
        bool hasData = messages.Any(m => m.Contents.OfType<DataContent>().Any());
        if (!hasData) return messages;

        var caps = provider.Capabilities;
        var filtered = new List<ChatMessage>(messages.Count);
        foreach (ChatMessage msg in messages)
        {
            bool hasDataContent = msg.Contents.OfType<DataContent>().Any();
            if (!hasDataContent)
            {
                filtered.Add(msg);
                continue;
            }

            var kept = new List<AIContent>(msg.Contents.Count);
            foreach (AIContent content in msg.Contents)
            {
                if (content is not DataContent dc)
                {
                    kept.Add(content);
                    continue;
                }

                bool supported = dc.MediaType?.ToLowerInvariant() switch
                {
                    { } m when m.StartsWith("image/", StringComparison.Ordinal) => caps.Inputs.HasFlag(InputModality.Image),
                    { } m when m.StartsWith("audio/", StringComparison.Ordinal) => caps.Inputs.HasFlag(InputModality.Audio),
                    { } m when m.StartsWith("video/", StringComparison.Ordinal) => caps.Inputs.HasFlag(InputModality.Video),
                    _ => caps.Inputs.HasFlag(InputModality.File)
                };

                if (supported)
                    kept.Add(content);
                else
                    logger.LogWarning(
                        "DataContent ({MimeType}) skipped: provider '{Provider}' does not support this modality",
                        dc.MediaType, provider.DisplayName);
            }

            filtered.Add(new ChatMessage(msg.Role, kept) { AuthorName = msg.AuthorName });
        }

        return filtered;
    }
}
