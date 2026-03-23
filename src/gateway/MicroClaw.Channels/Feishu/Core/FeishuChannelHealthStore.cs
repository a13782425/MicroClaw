using System.Collections.Concurrent;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-F-2: 记录各飞书渠道最近一条消息的处理结果，供健康检查端点读取。
/// </summary>
public sealed class FeishuChannelHealthStore
{
    private sealed record HealthEntry(DateTimeOffset ProcessedAt, bool Success, string? Error);

    private readonly ConcurrentDictionary<string, HealthEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>记录渠道最近一次消息处理结果。</summary>
    public void Report(string channelId, bool success, string? error = null)
        => _entries[channelId] = new HealthEntry(DateTimeOffset.UtcNow, success, error);

    /// <summary>获取渠道最近一次消息处理结果。若无记录，三个输出均为 null。</summary>
    public (DateTimeOffset? ProcessedAt, bool? Success, string? Error) GetLastMessage(string channelId)
    {
        if (_entries.TryGetValue(channelId, out HealthEntry? e))
            return (e.ProcessedAt, e.Success, e.Error);
        return (null, null, null);
    }
}
