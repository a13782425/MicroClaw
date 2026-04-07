using System.Collections.Concurrent;
using MicroClaw.Channels;
using MicroClaw.Channels.Feishu;
using MicroClaw.Configuration.Options;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Sessions;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Jobs;

/// <summary>
/// F-C-7: 飞书对话摘要定时同步 Job。
/// 定期扫描所有启用了 <see cref="FeishuChannelSettings.SummaryDocToken"/> 的飞书渠道，
/// 将各会话在上次同步以来的新消息追加到对应飞书文档，实现对话归档。
/// </summary>
/// <remarks>
/// <para>每 2 分钟检查一次；每个渠道按自身 <c>SummaryIntervalMinutes</c>（默认 60 分钟）独立控制实际触发频率。</para>
/// <para>
/// 注意：<see cref="SessionEntity"/> 未存储渠道 ID，因此同步对象为所有
/// <c>ChannelType == Feishu</c> 的会话。多飞书渠道均配置 SummaryDocToken 时，
/// 每个渠道的文档将收到所有飞书会话的摘要。
/// </para>
/// <para>最后同步时间仅保存在内存中，服务重启后将从"当前时间 - 同步间隔"重新计算，
/// 避免历史多次重复追加。</para>
/// </remarks>
public sealed class FeishuDocSyncJob(
    ChannelConfigStore channelConfigStore,
    ISessionRepository repo,
    ILogger<FeishuDocSyncJob> logger) : IScheduledJob
{
    public string JobName => "feishu-doc-sync";
    public JobSchedule Schedule => new JobSchedule.FixedInterval(TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(30));

    /// <summary>Key = channelId，Value = 该渠道最后一次同步完成时间（UTC）。</summary>
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastSyncAt = new();

    public async Task ExecuteAsync(CancellationToken ct)
    {
        await SyncAllChannelsAsync(ct);
    }

    /// <summary>扫描所有启用了 SummaryDocToken 的飞书渠道，并按各自间隔决定是否触发同步。</summary>
    private async Task SyncAllChannelsAsync(CancellationToken ct)
    {
        foreach (ChannelEntity config in channelConfigStore.All)
        {
            if (ct.IsCancellationRequested) break;

            if (config.ChannelType != ChannelType.Feishu || !config.IsEnabled) continue;

            FeishuChannelSettings? settings = FeishuChannelSettings.TryParse(config.SettingJson);
            if (settings is null
                || string.IsNullOrWhiteSpace(settings.SummaryDocToken)
                || settings.SummaryIntervalMinutes <= 0)
                continue;

            TimeSpan interval = TimeSpan.FromMinutes(settings.SummaryIntervalMinutes);

            // 首次遇到该渠道：将 lastSync 初始化为「当前时间 - 间隔」，下一个 tick 前不会立即触发
            DateTimeOffset lastSync = _lastSyncAt.GetOrAdd(
                config.Id,
                _ => DateTimeOffset.UtcNow - interval + TimeSpan.FromMinutes(2)); // 首次约等一个检查间隔后触发

            if (DateTimeOffset.UtcNow - lastSync < interval) continue;

            logger.LogInformation(
                "F-C-7 FeishuDocSyncJob 开始同步渠道 {ChannelId}（{ChannelName}）→ 文档 {DocToken}",
                config.Id, config.DisplayName, settings.SummaryDocToken);

            DateTimeOffset syncFrom = lastSync;
            await SyncChannelAsync(config, settings, syncFrom, ct);

            // 无论局部失败，都推进时间戳避免卡住
            _lastSyncAt[config.Id] = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// 为指定飞书渠道同步所有飞书会话在 <paramref name="fromUtc"/> 之后的新消息到文档。
    /// </summary>
    private async Task SyncChannelAsync(
        ChannelEntity config,
        FeishuChannelSettings settings,
        DateTimeOffset fromUtc,
        CancellationToken ct)
    {
        // 取所有飞书会话（SessionEntity 未存储 channelId，故取全部 Feishu 会话）
        IReadOnlyList<Session> feishuSessions = repo.GetAll()
            .Where(s => s.ChannelType == ChannelType.Feishu)
            .ToList();

        if (feishuSessions.Count == 0)
        {
            logger.LogDebug("F-C-7 渠道 {ChannelId} 暂无飞书会话，跳过", config.Id);
            return;
        }

        int syncedCount = 0;
        int skippedCount = 0;

        foreach (Session session in feishuSessions)
        {
            if (ct.IsCancellationRequested) break;

            IReadOnlyList<SessionMessage> messages =
                repo.GetMessages(session.Id);

            if (messages.Count == 0) continue;

            (bool success, string? error) = await FeishuDocTools.AppendSessionSummaryAsync(
                settings, session.Title, messages, fromUtc, logger, ct);

            if (success && error is null)
                syncedCount++;
            else if (success) // success=true, error=null means "skipped (no new messages)"
                skippedCount++;
            else
                logger.LogWarning(
                    "F-C-7 同步会话 {SessionId}（{SessionTitle}）失败: {Error}",
                    session.Id, session.Title, error);
        }

        logger.LogInformation(
            "F-C-7 渠道 {ChannelId} 同步完成：已追加={Synced} 跳过={Skipped} 总会话={Total}",
            config.Id, syncedCount, skippedCount, feishuSessions.Count);
    }
}
