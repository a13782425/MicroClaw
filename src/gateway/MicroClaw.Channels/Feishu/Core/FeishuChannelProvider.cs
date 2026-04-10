using System.Collections.Concurrent;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// 有状态 BackgroundService，管理所有飞书渠道实例的生命周期。
/// 持有 <see cref="FeishuChannel"/> 实例字典，替代已删除的 <c>FeishuWebSocketManager</c>，
/// 负责 WebSocket 渠道的定时同步与断线重连。
/// </summary>
internal sealed class FeishuChannelProvider : BackgroundService, IChannelProvider, IFeishuWebSocketSync
{
    private readonly ChannelService _channelStore;
    private readonly FeishuMessageProcessor _processor;
    private readonly ILoggerFactory _loggerFactory;
    private readonly FeishuChannelHealthStore _healthStore;
    private readonly FeishuChannelStatsService _statsService;
    private readonly ILogger<FeishuChannelProvider> _logger;

    /// <summary>活跃的飞书渠道实例字典，key = channelId。</summary>
    private readonly ConcurrentDictionary<string, FeishuChannel> _instances = new(StringComparer.OrdinalIgnoreCase);

    public FeishuChannelProvider(
        ChannelService channelStore,
        FeishuMessageProcessor processor,
        ILoggerFactory loggerFactory,
        FeishuChannelHealthStore healthStore,
        FeishuChannelStatsService statsService)
    {
        _channelStore = channelStore;
        _processor = processor;
        _loggerFactory = loggerFactory;
        _healthStore = healthStore;
        _statsService = statsService;
        _logger = loggerFactory.CreateLogger<FeishuChannelProvider>();
    }

    // ── IChannelProvider ────────────────────────────────────────────────

    public string Name => "Feishu";
    public ChannelType Type => ChannelType.Feishu;
    public string DisplayName => "飞书";

    public IChannel Create(ChannelEntity config)
    {
        // For Webhook channels: create lightweight instance on demand.
        // For WebSocket channels: return from _instances cache (managed by BackgroundService).
        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(config.SettingJson) ?? new();
        bool isWebSocket = string.Equals(settings.ConnectionMode, "websocket", StringComparison.OrdinalIgnoreCase);

        if (isWebSocket && _instances.TryGetValue(config.Id, out FeishuChannel? existing))
            return existing;

        // Create synchronously for Webhook mode; for WebSocket, block once (instance will be cached next call)
        FeishuChannel channel = FeishuChannel.CreateAsync(config, settings, this, _processor, _loggerFactory)
            .GetAwaiter().GetResult();

        if (isWebSocket)
            _instances.TryAdd(config.Id, channel);

        return channel;
    }

    public Task PublishAsync(ChannelEntity config, ChannelMessage message, CancellationToken cancellationToken = default)
    {
        if (_instances.TryGetValue(config.Id, out FeishuChannel? ch))
            return ch.PublishAsync(message, cancellationToken);

        return Create(config).PublishAsync(message, cancellationToken);
    }

    public Task<WebhookResult> HandleWebhookAsync(ChannelEntity config, string body,
        IReadOnlyDictionary<string, string?>? headers = null, CancellationToken cancellationToken = default)
    {
        if (_instances.TryGetValue(config.Id, out FeishuChannel? ch))
            return ch.HandleWebhookAsync(body, headers, cancellationToken);

        return Create(config).HandleWebhookAsync(body, headers, cancellationToken);
    }

    public Task<ChannelTestResult> TestConnectionAsync(ChannelEntity config, CancellationToken cancellationToken = default)
        => Create(config).TestConnectionAsync(cancellationToken);

    public Task<ChannelDiagnostics> GetDiagnosticsAsync(ChannelEntity config, CancellationToken cancellationToken = default)
    {
        FeishuChannelSettings? settings = FeishuChannelSettings.TryParse(config.SettingJson);
        string connectionMode = settings?.ConnectionMode ?? "webhook";

        string connectionStatus;
        if (!config.IsEnabled)
            connectionStatus = "disabled";
        else if (string.Equals(connectionMode, "websocket", StringComparison.OrdinalIgnoreCase))
            connectionStatus = _instances.ContainsKey(config.Id) ? "connected" : "disconnected";
        else
            connectionStatus = "webhook";

        var (lastAt, lastSuccess, lastError) = _healthStore.GetLastMessage(config.Id);
        var (sigFail, aiFail, replyFail) = _statsService.GetStats(config.Id);

        Dictionary<string, object?> extra = new()
        {
            ["connectionMode"] = connectionMode,
            ["connectionStatus"] = connectionStatus,
            ["lastMessageAt"] = lastAt,
            ["lastMessageSuccess"] = lastSuccess,
            ["lastMessageError"] = lastError,
            ["signatureFailures"] = sigFail,
            ["aiCallFailures"] = aiFail,
            ["replyFailures"] = replyFail,
        };

        return Task.FromResult(new ChannelDiagnostics(config.Id, "feishu", connectionStatus, extra));
    }

    // ── BackgroundService (replaces FeishuWebSocketManager) ─────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial sync on startup
        await SyncChannelsAsync(stoppingToken);
    }

    /// <summary>对比期望状态与实际状态，启停对应 WebSocket 渠道实例。</summary>
    public async Task SyncChannelsAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ChannelEntity> feishuChannels = _channelStore.GetConfigsByType(ChannelType.Feishu);

        HashSet<string> desiredIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (ChannelEntity channel in feishuChannels)
        {
            if (!channel.IsEnabled) continue;
            FeishuChannelSettings? settings = FeishuChannelSettings.TryParse(channel.SettingJson);
            if (settings is null) continue;
            if (!string.Equals(settings.ConnectionMode, "websocket", StringComparison.OrdinalIgnoreCase)) continue;

            desiredIds.Add(channel.Id);

            if (!_instances.ContainsKey(channel.Id))
                await StartInstanceAsync(channel, settings, ct);
        }

        // Remove instances that are no longer needed
        foreach (string existingId in _instances.Keys.ToArray())
        {
            if (!desiredIds.Contains(existingId))
                await RemoveAsync(existingId);
        }
    }

    /// <summary>移除并释放指定渠道实例。</summary>
    public async Task RemoveAsync(string channelId)
    {
        if (!_instances.TryRemove(channelId, out FeishuChannel? ch)) return;
        _logger.LogInformation("停止飞书渠道实例 channel={ChannelId}", channelId);
        try { await ch.DisposeAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "释放飞书渠道实例异常 channel={ChannelId}", channelId); }
    }

    private async Task StartInstanceAsync(ChannelEntity channel, FeishuChannelSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.AppId) || string.IsNullOrWhiteSpace(settings.AppSecret))
        {
            _logger.LogError("飞书 WebSocket 启动失败：AppId 或 AppSecret 为空 channel={ChannelId}", channel.Id);
            return;
        }

        try
        {
            _logger.LogInformation("启动飞书 WebSocket 实例 channel={ChannelId} appId={AppId}", channel.Id, settings.AppId);
            FeishuChannel ch = await FeishuChannel.CreateAsync(channel, settings, this, _processor, _loggerFactory, ct);
            _instances[channel.Id] = ch;

            // Start per-connection reconnect monitor as a fire-and-forget background Task
            _ = Task.Run(() => MonitorConnectionAsync(channel, settings, ch, ct), CancellationToken.None);

            _logger.LogInformation("飞书 WebSocket 实例已启动 channel={ChannelId}", channel.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "飞书 WebSocket 实例启动失败 channel={ChannelId}", channel.Id);
        }
    }

    /// <summary>
    /// 监控指定渠道 WssService 生命周期；断线时使用指数退避重连（5s→10s→20s→40s→60s 封顶）。
    /// </summary>
    private async Task MonitorConnectionAsync(
        ChannelEntity channel,
        FeishuChannelSettings settings,
        FeishuChannel instance,
        CancellationToken stoppingToken)
    {
        // Obtain the WssService's ExecuteTask via reflection on BackgroundService
        Task? wssTask = instance.GetType()
            .GetProperty("WssExecuteTask", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?
            .GetValue(instance) as Task;

        // Fallback: just wait until stoppingToken is cancelled (SDK handles reconnect internally)
        if (wssTask is null)
        {
            try { await Task.Delay(Timeout.Infinite, stoppingToken); }
            catch (OperationCanceledException) { }
            return;
        }

        try { await wssTask.WaitAsync(stoppingToken); }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WssService 异常退出 channel={ChannelId}", channel.Id);
        }

        if (wssTask.IsCompletedSuccessfully)
        {
            try { await Task.Delay(Timeout.Infinite, stoppingToken); }
            catch (OperationCanceledException) { }
            return;
        }

        if (stoppingToken.IsCancellationRequested) return;

        _logger.LogWarning("飞书 WebSocket 断线 channel={ChannelId}，准备重连", channel.Id);

        // Remove old instance
        if (_instances.TryRemove(channel.Id, out FeishuChannel? old))
        {
            try { await old.DisposeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "释放断线实例异常 channel={ChannelId}", channel.Id); }
        }

        // Exponential backoff: 5s → 10s → 20s → 40s → 60s (cap)
        int delaySeconds = 5;
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("飞书 WebSocket 重连等待 {Delay}s channel={ChannelId}", delaySeconds, channel.Id);
            try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken); }
            catch (OperationCanceledException) { return; }

            delaySeconds = Math.Min(delaySeconds * 2, 60);

            ChannelEntity? current = _channelStore.GetConfigsByType(ChannelType.Feishu)
                .FirstOrDefault(c => c.Id == channel.Id);
            if (current is null || !current.IsEnabled) return;

            FeishuChannelSettings? currentSettings = FeishuChannelSettings.TryParse(current.SettingJson);
            if (currentSettings is null ||
                !string.Equals(currentSettings.ConnectionMode, "websocket", StringComparison.OrdinalIgnoreCase))
                return;

            if (_instances.ContainsKey(channel.Id)) return;

            try
            {
                await StartInstanceAsync(current, currentSettings, stoppingToken);
                _logger.LogInformation("飞书 WebSocket 重连成功 channel={ChannelId}", channel.Id);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "飞书 WebSocket 重连失败 channel={ChannelId}，{Delay}s 后重试", channel.Id, delaySeconds);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (string id in _instances.Keys.ToArray())
            await RemoveAsync(id);
        await base.StopAsync(cancellationToken);
    }

    /// <summary>记录 Webhook 签名验证失败（由 <see cref="FeishuChannel"/> 调用）。</summary>
    internal void ReportSignatureFailure(string channelId) => _statsService.IncrementSignatureFailure(channelId);

    /// <summary>返回指定渠道 ID 的连接状态（用于 <see cref="IFeishuWebSocketSync"/> 兼容）。</summary>
    public string GetConnectionStatus(string channelId)
        => _instances.ContainsKey(channelId) ? "connected" : "disconnected";

    /// <summary>
    /// 获取指定渠道的活跃实例 <see cref="IFeishuTenantApi"/>，供工具工厂按需调用。
    /// </summary>
    internal bool TryGetApi(string channelId, out FeishuNetSdk.IFeishuTenantApi? api)
    {
        if (_instances.TryGetValue(channelId, out FeishuChannel? ch))
        {
            api = ch.Api;
            return true;
        }
        api = null;
        return false;
    }
}
