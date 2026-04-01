using MicroClaw.Abstractions;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Services;

/// <summary>
/// F-D-1: 基于 SQLite 的渠道消息重试队列实现。
/// AI 调用失败时将消息持久化到 channel_retry_queue 表，由 ChannelRetryJob 定期重试。
/// </summary>
public sealed class ChannelRetryQueueService(
    IDbContextFactory<GatewayDbContext> dbFactory,
    ILogger<ChannelRetryQueueService> logger) : IChannelRetryQueue
{
    public async Task EnqueueAsync(
        string channelType,
        string channelId,
        string sessionId,
        string messageId,
        string userText,
        string errorMessage,
        CancellationToken ct = default)
    {
        await using GatewayDbContext db = await dbFactory.CreateDbContextAsync(ct);

        // 幂等：同一 messageId 已在队列中则跳过（唯一索引保护）
        bool exists = await db.ChannelRetryQueue
            .AnyAsync(e => e.MessageId == messageId, ct);
        if (exists)
        {
            logger.LogDebug("F-D-1 重试队列：messageId={MessageId} 已存在，跳过入队", messageId);
            return;
        }

        long nowMs = TimeBase.NowMs();
        ChannelRetryQueueEntity entity = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            ChannelType = channelType,
            ChannelId = channelId,
            SessionId = sessionId,
            MessageId = messageId,
            UserText = userText,
            RetryCount = 0,
            Status = "pending",
            NextRetryAtMs = nowMs,
            CreatedAtMs = nowMs,
            LastErrorMessage = errorMessage.Length > 500 ? errorMessage[..500] : errorMessage,
        };

        db.ChannelRetryQueue.Add(entity);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("F-D-1 消息已加入重试队列 messageId={MessageId} channelId={ChannelId}",
            messageId, channelId);
    }

    /// <summary>获取所有 pending 且到期的条目。</summary>
    public async Task<List<ChannelRetryQueueEntity>> GetPendingAsync(CancellationToken ct = default)
    {
        long nowMs = TimeBase.NowMs();
        await using GatewayDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await db.ChannelRetryQueue
            .Where(e => e.Status == "pending" && e.NextRetryAtMs <= nowMs)
            .ToListAsync(ct);
    }

    /// <summary>重试成功后从队列移除。</summary>
    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        await using GatewayDbContext db = await dbFactory.CreateDbContextAsync(ct);
        ChannelRetryQueueEntity? entity = await db.ChannelRetryQueue.FindAsync([id], ct);
        if (entity is null) return;
        db.ChannelRetryQueue.Remove(entity);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>更新重试次数和下次重试时间（指数退避），超过 maxRetries 后标记为 exhausted。</summary>
    public async Task UpdateRetryAsync(string id, int newRetryCount, int maxRetries,
        string errorMessage, CancellationToken ct = default)
    {
        await using GatewayDbContext db = await dbFactory.CreateDbContextAsync(ct);
        ChannelRetryQueueEntity? entity = await db.ChannelRetryQueue.FindAsync([id], ct);
        if (entity is null) return;

        entity.RetryCount = newRetryCount;
        entity.LastErrorMessage = errorMessage.Length > 500 ? errorMessage[..500] : errorMessage;

        if (newRetryCount >= maxRetries)
        {
            entity.Status = "exhausted";
        }
        else
        {
            // 指数退避：60s → 120s → 240s
            int delaySeconds = 60 * (int)Math.Pow(2, newRetryCount - 1);
            entity.NextRetryAtMs = TimeBase.ToMs(DateTimeOffset.UtcNow.AddSeconds(delaySeconds));
        }

        await db.SaveChangesAsync(ct);
    }
}
