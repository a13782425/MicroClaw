using System.Collections.Concurrent;
using FeishuNetSdk;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Channel;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration.Models;
using MicroClaw.Configuration.Options;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// Feishu channel provider — manages all Feishu channel instances.
/// NOT a BackgroundService — lifecycle driven by <c>ChannelRunner</c>.
/// Holds RateLimiter / HealthStore / StatsService / MessageProcessor for all instances.
/// </summary>
internal sealed class FeishuChannelProvider : IChannelProvider
{
    private readonly ChannelService _channelStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<FeishuChannelProvider> _logger;

    /// <summary>Active Feishu channel instances, key = channelId.</summary>
    private readonly ConcurrentDictionary<string, FeishuChannel> _instances = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Config fingerprint for each managed instance, used by TickAsync to detect changes.</summary>
    private readonly ConcurrentDictionary<string, string> _fingerprints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per-AppId token-bucket rate limiter (QPS ≤ 5).</summary>
    internal FeishuRateLimiter RateLimiter { get; } = new();

    /// <summary>Tracks last message processing result per channel.</summary>
    internal FeishuChannelHealthStore HealthStore { get; } = new();

    /// <summary>Error statistics per channel (signature/AI/reply failures).</summary>
    internal FeishuChannelStatsService StatsService { get; } = new();

    /// <summary>Shared message processor, created lazily when first needed (after DI container is built).</summary>
    private FeishuMessageProcessor? _processor;
    private readonly Lock _processorLock = new();

    internal FeishuMessageProcessor Processor
    {
        get
        {
            if (_processor is not null) return _processor;
            lock (_processorLock)
            {
                return _processor ??= new FeishuMessageProcessor(
                    _serviceProvider.GetRequiredService<ISessionService>(),
                    _loggerFactory.CreateLogger<FeishuMessageProcessor>(),
                    _serviceProvider.GetService<IAgentMessageHandler>(),
                    _serviceProvider.GetService<IChannelRetryQueue>(),
                    RateLimiter, HealthStore, StatsService);
            }
        }
    }

    public FeishuChannelProvider(
        ChannelService channelStore,
        IServiceProvider serviceProvider,
        ILoggerFactory loggerFactory)
    {
        _channelStore = channelStore;
        _serviceProvider = serviceProvider;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<FeishuChannelProvider>();
    }

    // ── IChannelProvider ────────────────────────────────────────────────

    public string Name => "Feishu";
    public ChannelType Type => ChannelType.Feishu;
    public string DisplayName => "飞书";

    public IChannel Create(ChannelEntity config)
    {
        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(config.SettingJson) ?? new();
        bool isWebSocket = string.Equals(settings.ConnectionMode, "websocket", StringComparison.OrdinalIgnoreCase);

        if (isWebSocket && _instances.TryGetValue(config.Id, out FeishuChannel? existing))
            return existing;

        FeishuChannel channel = FeishuChannel.CreateAsync(config, settings, this, _loggerFactory)
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

        // Enrich diagnostics with health + stats data
        (DateTimeOffset? lastAt, bool? lastOk, string? lastError) = HealthStore.GetLastMessage(config.Id);
        (long sigFail, long aiFail, long replyFail) = StatsService.GetStats(config.Id);

        Dictionary<string, object?> extra = new()
        {
            ["connectionMode"] = connectionMode,
            ["connectionStatus"] = connectionStatus,
            ["lastMessageAt"] = lastAt?.ToString("o"),
            ["lastMessageSuccess"] = lastOk,
            ["lastMessageError"] = lastError,
            ["signatureFailures"] = sigFail,
            ["aiCallFailures"] = aiFail,
            ["replyFailures"] = replyFail,
        };

        return Task.FromResult(new ChannelDiagnostics(config.Id, "feishu", connectionStatus, extra));
    }

    // ── Lifecycle Hooks (driven by ChannelRunner) ───────────────────────

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("飞书 Provider 启动，开始扫描渠道配置…");

        IReadOnlyList<ChannelEntity> configs = _channelStore.GetConfigsByType(ChannelType.Feishu);
        int started = 0;

        foreach (ChannelEntity config in configs)
        {
            if (!config.IsEnabled)
            {
                _logger.LogDebug("跳过已禁用的飞书渠道 channel={ChannelId}", config.Id);
                continue;
            }

            FeishuChannelSettings settings = FeishuChannelSettings.TryParse(config.SettingJson) ?? new();
            if (!string.Equals(settings.ConnectionMode, "websocket", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("跳过 Webhook 模式飞书渠道 channel={ChannelId}（按需创建）", config.Id);
                continue;
            }

            try
            {
                await StartInstanceAsync(config, settings, cancellationToken);
                started++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "启动飞书渠道实例失败 channel={ChannelId} appId={AppId}",
                    config.Id, settings.AppId);
            }
        }

        _logger.LogInformation("飞书 Provider 启动完成，已启动 {Count} 个 WebSocket 实例", started);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("飞书 Provider 停止，释放 {Count} 个实例…", _instances.Count);

        foreach (string id in _instances.Keys.ToArray())
            await RemoveAsync(id);

        RateLimiter.Dispose();
        _logger.LogInformation("飞书 Provider 已停止");
    }

    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ChannelEntity> configs = _channelStore.GetConfigsByType(ChannelType.Feishu);

        // Build desired state: enabled WebSocket channels
        HashSet<string> desiredIds = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (ChannelEntity Config, FeishuChannelSettings Settings)> desiredMap = new(StringComparer.OrdinalIgnoreCase);

        foreach (ChannelEntity config in configs)
        {
            FeishuChannelSettings settings = FeishuChannelSettings.TryParse(config.SettingJson) ?? new();
            if (!config.IsEnabled ||
                !string.Equals(settings.ConnectionMode, "websocket", StringComparison.OrdinalIgnoreCase))
                continue;

            desiredIds.Add(config.Id);
            desiredMap[config.Id] = (config, settings);
        }

        // Remove instances that are no longer desired (deleted / disabled / switched to webhook)
        foreach (string id in _instances.Keys.ToArray())
        {
            if (desiredIds.Contains(id)) continue;

            _logger.LogInformation("飞书渠道已移除或禁用，停止实例 channel={ChannelId}", id);
            await RemoveAsync(id);
        }

        // Add or recreate instances
        foreach ((string id, (ChannelEntity config, FeishuChannelSettings settings)) in desiredMap)
        {
            string newFingerprint = BuildFingerprint(config);

            if (_instances.ContainsKey(id))
            {
                // Check if config changed — if so, recreate
                if (_fingerprints.TryGetValue(id, out string? oldFingerprint) && oldFingerprint == newFingerprint)
                    continue;

                _logger.LogInformation("飞书渠道配置变更，重建实例 channel={ChannelId}", id);
                await RemoveAsync(id);
            }

            try
            {
                await StartInstanceAsync(config, settings, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TickAsync 启动飞书渠道实例失败 channel={ChannelId}", id);
            }
        }
    }

    // ── Internal Helpers ────────────────────────────────────────────────

    /// <summary>Create and register a WebSocket channel instance.</summary>
    private async Task StartInstanceAsync(ChannelEntity config, FeishuChannelSettings settings, CancellationToken ct)
    {
        FeishuChannel channel = await FeishuChannel.CreateAsync(config, settings, this, _loggerFactory, ct);
        _instances[config.Id] = channel;
        _fingerprints[config.Id] = BuildFingerprint(config);

        _logger.LogInformation("飞书渠道实例已启动 channel={ChannelId} appId={AppId} mode={Mode}",
            config.Id, settings.AppId, settings.ConnectionMode);
    }

    /// <summary>Remove and dispose a channel instance.</summary>
    internal async Task RemoveAsync(string channelId)
    {
        _fingerprints.TryRemove(channelId, out _);
        if (!_instances.TryRemove(channelId, out FeishuChannel? ch)) return;
        _logger.LogInformation("停止飞书渠道实例 channel={ChannelId}", channelId);
        try { await ch.DisposeAsync(); }
        catch (Exception ex) { _logger.LogWarning(ex, "释放飞书渠道实例异常 channel={ChannelId}", channelId); }
    }

    /// <summary>Report webhook signature failure (called by FeishuChannel).</summary>
    internal void ReportSignatureFailure(string channelId)
    {
        StatsService.IncrementSignatureFailure(channelId);
        _logger.LogWarning("飞书 Webhook 签名验证失败 channel={ChannelId}", channelId);
    }

    /// <summary>Get active IFeishuTenantApi for a channel (used by tool factory).</summary>
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

    // ── Tool Management (IChannelProvider) ──────────────────────────────

    /// <summary>Returns all Feishu tool descriptions from the 5 tool classes.</summary>
    public IReadOnlyList<(string Name, string Description)> GetToolDescriptions() =>
        [.. FeishuDocTools.GetToolDescriptions(),
         .. FeishuBitableTools.GetToolDescriptions(),
         .. FeishuWikiTools.GetToolDescriptions(),
         .. FeishuCalendarTools.GetToolDescriptions(),
         .. FeishuApprovalTools.GetToolDescriptions()];

    /// <summary>
    /// Creates Feishu tools for a specific channel instance.
    /// For WebSocket channels, reuses the cached API; for webhook channels, creates a temporary channel.
    /// </summary>
    public Task<IReadOnlyList<AIFunction>> CreateToolsAsync(string channelId, CancellationToken cancellationToken = default)
    {
        ChannelEntity? config = _channelStore.GetById(channelId);
        if (config is null)
            return Task.FromResult<IReadOnlyList<AIFunction>>([]);

        FeishuChannelSettings settings = FeishuChannelSettings.TryParse(config.SettingJson) ?? new();
        if (string.IsNullOrWhiteSpace(settings.AppId) || string.IsNullOrWhiteSpace(settings.AppSecret))
        {
            _logger.LogWarning("飞书渠道 {ChannelId} AppId/AppSecret 未配置，跳过飞书工具创建", channelId);
            return Task.FromResult<IReadOnlyList<AIFunction>>([]);
        }

        // Try cached WebSocket instance first; fall back to creating a temporary channel for webhook mode
        IFeishuTenantApi api;
        if (_instances.TryGetValue(channelId, out FeishuChannel? cached))
        {
            api = cached.Api;
        }
        else
        {
            IChannel ch = Create(config);
            if (ch is FeishuChannel feishuCh)
                api = feishuCh.Api;
            else
                return Task.FromResult<IReadOnlyList<AIFunction>>([]);
        }

        ILogger toolLogger = _loggerFactory.CreateLogger("MicroClaw.Channels.Feishu.Tools");

        IReadOnlyList<AIFunction> tools =
            [.. FeishuDocTools.CreateTools(settings, api, toolLogger),
             .. FeishuBitableTools.CreateTools(settings, api, toolLogger),
             .. FeishuWikiTools.CreateTools(settings, api, toolLogger),
             .. FeishuCalendarTools.CreateTools(settings, api, toolLogger),
             .. FeishuApprovalTools.CreateTools(settings, api, toolLogger)];

        return Task.FromResult(tools);
    }

    private static string BuildFingerprint(ChannelEntity config)
        => string.Join("|", config.Id, config.IsEnabled, config.SettingJson);
}
