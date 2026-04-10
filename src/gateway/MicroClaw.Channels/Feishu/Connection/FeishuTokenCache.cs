using System.Collections.Concurrent;
using FeishuNetSdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// F-D-3: 按 AppId 缓存飞书 ServiceProvider，复用 SDK 内部的 Tenant Access Token，
/// 避免每次 API 调用都重新鉴权。缓存有效期为 1 小时 50 分钟（Token 实际有效期 2 小时，提前 10 分钟刷新）。
/// </summary>
internal sealed class FeishuTokenCache(ILogger<FeishuTokenCache> logger) : IDisposable
{
    private sealed record CachedEntry(ServiceProvider Sp, DateTimeOffset ExpiresAt);

    // Feishu Tenant Access Token 有效期 7200 秒（2 小时），提前 10 分钟刷新
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(2) - TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, CachedEntry> _entries = new();
    private bool _disposed;

    /// <summary>
    /// 获取或创建（并缓存）与指定 AppId 对应的 <see cref="IFeishuTenantApi"/> 实例。
    /// 线程安全：并发刷新时仅可能短暂多创建一个 ServiceProvider，旧/冗余实例异步释放。
    /// </summary>
    public IFeishuTenantApi GetOrCreateApi(FeishuChannelSettings settings)
    {
        string key = settings.AppId ?? string.Empty;

        // 快速路径：缓存命中
        if (_entries.TryGetValue(key, out CachedEntry? cached) && DateTimeOffset.UtcNow < cached.ExpiresAt)
            return cached.Sp.GetRequiredService<IFeishuTenantApi>();

        // 慢路径：构建新 ServiceProvider
        ServiceProvider newSp = FeishuMessageProcessor.BuildFeishuServiceProvider(settings);
        CachedEntry newEntry = new(newSp, DateTimeOffset.UtcNow.Add(CacheTtl));
        logger.LogDebug("飞书 Token 缓存刷新 appId={AppId}，下次刷新时间 {ExpiresAt:HH:mm:ss}",
            key, newEntry.ExpiresAt);

        // 首次添加（此 key 尚未有缓存）
        if (_entries.TryAdd(key, newEntry))
            return newSp.GetRequiredService<IFeishuTenantApi>();

        // 已存在：CAS 更新，防止并发重复刷新
        while (true)
        {
            if (!_entries.TryGetValue(key, out CachedEntry? current))
            {
                // 极罕见：Dispose 期间并发，直接返回本次新建的
                return newSp.GetRequiredService<IFeishuTenantApi>();
            }

            if (DateTimeOffset.UtcNow < current.ExpiresAt)
            {
                // 另一线程已抢先刷新，丢弃本次新建的 SP
                _ = newSp.DisposeAsync().AsTask();
                return current.Sp.GetRequiredService<IFeishuTenantApi>();
            }

            // 原子替换
            if (_entries.TryUpdate(key, newEntry, current))
            {
                _ = current.Sp.DisposeAsync().AsTask(); // 异步释放旧 SP
                return newSp.GetRequiredService<IFeishuTenantApi>();
            }
            // CAS 失败（另一线程已变更），重试
        }
    }

    /// <summary>
    /// F-F-2: 返回指定 AppId 对应缓存 Token 的剩余有效时间；未缓存时返回 null。
    /// </summary>
    public TimeSpan? GetRemainingTtl(string appId)
    {
        if (_entries.TryGetValue(appId, out CachedEntry? e))
        {
            TimeSpan remaining = e.ExpiresAt - DateTimeOffset.UtcNow;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (CachedEntry e in _entries.Values)
            e.Sp.Dispose();
        _entries.Clear();
    }
}
