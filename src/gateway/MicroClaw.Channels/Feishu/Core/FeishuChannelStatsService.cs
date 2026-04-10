using System.Collections.Concurrent;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-F-3: 统计各飞书渠道的错误事件次数（签名验证失败、AI 调用失败、回复失败）。
/// 内存级存储，重启清零；线程安全（Interlocked.Increment）。
/// </summary>
internal sealed class FeishuChannelStatsService
{
    private sealed class StatsEntry
    {
        public long SignatureFailures;
        public long AiCallFailures;
        public long ReplyFailures;
    }

    private readonly ConcurrentDictionary<string, StatsEntry> _stats =
        new(StringComparer.OrdinalIgnoreCase);

    private StatsEntry GetOrAdd(string channelId) =>
        _stats.GetOrAdd(channelId, _ => new StatsEntry());

    /// <summary>Webhook 签名验证失败时递增。</summary>
    public void IncrementSignatureFailure(string channelId) =>
        Interlocked.Increment(ref GetOrAdd(channelId).SignatureFailures);

    /// <summary>AI 模型调用失败时递增。</summary>
    public void IncrementAiCallFailure(string channelId) =>
        Interlocked.Increment(ref GetOrAdd(channelId).AiCallFailures);

    /// <summary>飞书回复 API 调用失败时递增。</summary>
    public void IncrementReplyFailure(string channelId) =>
        Interlocked.Increment(ref GetOrAdd(channelId).ReplyFailures);

    /// <summary>获取指定渠道的累计统计数据。若无记录，三项均返回 0。</summary>
    public (long SignatureFailures, long AiCallFailures, long ReplyFailures) GetStats(string channelId)
    {
        if (_stats.TryGetValue(channelId, out StatsEntry? e))
        {
            return (
                Interlocked.Read(ref e.SignatureFailures),
                Interlocked.Read(ref e.AiCallFailures),
                Interlocked.Read(ref e.ReplyFailures)
            );
        }
        return (0, 0, 0);
    }
}
