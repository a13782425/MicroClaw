using MicroClaw.Channels;
using MicroClaw.Channels.Feishu;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers;
using MicroClaw.Services;
using MicroClaw.Sessions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Jobs;

/// <summary>
/// F-D-1: 渠道消息失败重试后台任务。
/// 每 60 秒扫描 channel_retry_queue 表，对 pending 且到期的条目重新执行 AI 调用和飞书回复。
/// 指数退避（60s→120s→240s），最多重试 3 次；超次后标记为 exhausted 并记录 Critical 告警。
/// </summary>
public sealed class ChannelRetryJob(
    ChannelRetryQueueService retryQueueService,
    ChannelConfigStore channelConfigStore,
    ProviderConfigStore providerStore,
    ProviderClientFactory clientFactory,
    IChannelSessionService sessionService,
    ISessionRepository repo,
    FeishuMessageProcessor feishuProcessor,
    IAgentMessageHandler? agentHandler,
    ILogger<ChannelRetryJob> logger) : IScheduledJob
{
    private const int MaxRetries = 3;

    public string JobName => "channel-retry";
    public JobSchedule Schedule => new JobSchedule.FixedInterval(TimeSpan.FromSeconds(60), TimeSpan.Zero);

    public async Task ExecuteAsync(CancellationToken ct)
    {
        try
        {
            await ProcessPendingEntriesAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // ignore, normal shutdown
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "F-D-1 ChannelRetryJob 扫描周期发生未预期异常");
        }
    }

    private async Task ProcessPendingEntriesAsync(CancellationToken ct)
    {
        List<ChannelRetryQueueEntity> entries = await retryQueueService.GetPendingAsync(ct);
        if (entries.Count == 0) return;

        logger.LogInformation("F-D-1 ChannelRetryJob 发现 {Count} 条待重试消息", entries.Count);

        foreach (ChannelRetryQueueEntity entry in entries)
        {
            if (ct.IsCancellationRequested) break;
            await RetryOneAsync(entry, ct);
        }
    }

    private async Task RetryOneAsync(ChannelRetryQueueEntity entry, CancellationToken ct)
    {
        int attempt = entry.RetryCount + 1;
        logger.LogInformation("F-D-1 重试 attempt={Attempt}/{Max} messageId={MessageId}",
            attempt, MaxRetries, entry.MessageId);

        try
        {
            // 加载渠道配置
            ChannelConfig? channel = channelConfigStore.GetById(entry.ChannelId);
            if (channel is null || !channel.IsEnabled)
            {
                logger.LogWarning("F-D-1 渠道 {ChannelId} 不存在或已禁用，放弃重试 messageId={MessageId}",
                    entry.ChannelId, entry.MessageId);
                await retryQueueService.UpdateRetryAsync(entry.Id, MaxRetries, MaxRetries,
                    "渠道不存在或已禁用", ct);
                return;
            }

            FeishuChannelSettings settings = FeishuChannelSettings.TryParse(channel.SettingsJson) ?? new();

            // 获取会话历史
            IReadOnlyList<SessionMessage> history = sessionService.GetMessages(entry.SessionId);

            // 执行 AI 调用
            string aiReply;
            if (agentHandler?.HasAgentForChannel(channel.Id) == true)
            {
                AgentResponse agentResponse = await agentHandler.HandleMessageAsync(channel.Id, entry.SessionId, history, ct).MaterializeAsync(ct);
                aiReply = agentResponse.Text;
            }
            else
            {
                Session? session = repo.Get(entry.SessionId);
                string resolvedProviderId = session?.ProviderId ?? string.Empty;
                ProviderConfig? providerConfig = string.IsNullOrWhiteSpace(resolvedProviderId)
                    ? providerStore.GetDefault()
                    : providerStore.All.FirstOrDefault(p => p.Id == resolvedProviderId && p.IsEnabled);
                if (providerConfig is null || !providerConfig.IsEnabled)
                {
                    throw new InvalidOperationException(
                        $"找不到可用的 Provider（sessionId={entry.SessionId}）");
                }

                List<ChatMessage> chatMessages = history
                    .Select(m => new ChatMessage(
                        m.Role == "user" ? ChatRole.User : ChatRole.Assistant,
                        m.Content))
                    .ToList();

                IChatClient chatClient = clientFactory.Create(providerConfig);
                ChatResponse response = await chatClient.GetResponseAsync(chatMessages, cancellationToken: ct);
                aiReply = response.Text ?? "（无回复）";
            }

            // 保存助手消息并回复用户
            sessionService.AddMessage(entry.SessionId,
                new SessionMessage(Guid.NewGuid().ToString("N"), "assistant", aiReply, null, DateTimeOffset.UtcNow, null));

            await feishuProcessor.SendRetryReplyAsync(entry.MessageId, aiReply, settings, ct);

            // 成功，从队列移除
            await retryQueueService.DeleteAsync(entry.Id, ct);
            logger.LogInformation("F-D-1 重试成功，已移除队列 messageId={MessageId}", entry.MessageId);
        }
        catch (Exception ex)
        {
            string errorMsg = ex.Message;
            logger.LogWarning(ex, "F-D-1 重试 attempt={Attempt} 失败 messageId={MessageId}",
                attempt, entry.MessageId);

            await retryQueueService.UpdateRetryAsync(entry.Id, attempt, MaxRetries, errorMsg, ct);

            if (attempt >= MaxRetries)
            {
                logger.LogCritical(
                    "F-D-1 消息重试已耗尽（{Max}次），messageId={MessageId} channelId={ChannelId}，请人工排查。",
                    MaxRetries, entry.MessageId, entry.ChannelId);
            }
        }
    }
}
