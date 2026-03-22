using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// 飞书 API 调用令牌桶限流器（单例）。
/// <para>按 AppId 独立限流，单个 AppId QPS ≤ 5，符合飞书开放平台消息发送接口限制。</para>
/// <para>超频时令牌桶排队等待，而非直接失败，确保消息可靠送达。</para>
/// </summary>
public sealed class FeishuRateLimiter : IDisposable
{
    // 每个 AppId 独享一个令牌桶：5 个令牌 / 秒，队列最多允许 50 个请求等待
    private readonly ConcurrentDictionary<string, TokenBucketRateLimiter> _limiters = new();

    /// <summary>
    /// 等待获取指定 AppId 的 API 调用许可。
    /// 令牌充足时立即返回；超频时阻塞等待直至令牌补充。
    /// </summary>
    public async ValueTask WaitAsync(string appId, CancellationToken ct = default)
    {
        TokenBucketRateLimiter limiter = _limiters.GetOrAdd(appId, _ => new TokenBucketRateLimiter(
            new TokenBucketRateLimiterOptions
            {
                TokenLimit = 5,               // 桶容量：最多积攒 5 个令牌（应对突发）
                TokensPerPeriod = 5,          // 每周期补充 5 个
                ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 50,              // 最多排队 50 个请求，超出则直接拒绝
                AutoReplenishment = true
            }));

        using RateLimitLease lease = await limiter.AcquireAsync(permitCount: 1, ct);
        // QueueLimit 足够大，正常场景必然获得令牌；拒绝场景（队列满）在上层 catch 中静默处理
    }

    public void Dispose()
    {
        foreach (TokenBucketRateLimiter limiter in _limiters.Values)
            limiter.Dispose();
    }
}
