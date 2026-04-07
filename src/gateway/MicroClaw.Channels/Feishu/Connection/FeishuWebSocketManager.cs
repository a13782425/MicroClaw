using System.Collections.Concurrent;
using FeishuNetSdk.Services;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Configuration.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MicroClaw.Providers;

namespace MicroClaw.Channels.Feishu;

/// <summary>
/// 管理多个飞书 WebSocket 长连接。
/// 每个 WebSocket 模式的渠道拥有独立的 ServiceProvider（SDK 限制：一个容器只能建立一条连接）。
/// 启动时根据 ChannelConfigStore 中已启用的 WebSocket 渠道创建连接，并定期轮询配置变更。
/// </summary>
public sealed class FeishuWebSocketManager(
    ChannelConfigStore channelStore,
    ProviderConfigStore providerStore,
    ProviderClientFactory clientFactory,
    ISessionService sessionService,
    ILoggerFactory loggerFactory,
    FeishuTokenCache? tokenCache = null,
    IAgentMessageHandler? agentHandler = null) : BackgroundService
{
    private readonly ILogger<FeishuWebSocketManager> _logger = loggerFactory.CreateLogger<FeishuWebSocketManager>();
    private readonly ConcurrentDictionary<string, ChannelConnection> _connections = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动时同步一次；后续轮询由 FeishuWebSocketSyncJob（IScheduledJob/Quartz）驱动
        await SyncChannelsAsync(stoppingToken);
    }

    /// <summary>对比当前连接与数据库配置，启停对应渠道的 WebSocket 连接。</summary>
    public async Task SyncChannelsAsync(CancellationToken ct)
    {
        IReadOnlyList<ChannelEntity> feishuChannels = channelStore.GetByType(ChannelType.Feishu);

        // 需要活跃的渠道 ID 集合
        HashSet<string> desiredIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (ChannelEntity channel in feishuChannels)
        {
            if (!channel.IsEnabled) continue;

            FeishuChannelSettings? settings = FeishuChannelSettings.TryParse(channel.SettingJson);
            if (settings is null) continue;
            if (!string.Equals(settings.ConnectionMode, "websocket", StringComparison.OrdinalIgnoreCase))
                continue;

            desiredIds.Add(channel.Id);

            if (!_connections.ContainsKey(channel.Id))
            {
                await StartConnectionAsync(channel, settings, ct);
            }
        }

        // 移除不再需要的连接
        foreach (string existingId in _connections.Keys)
        {
            if (!desiredIds.Contains(existingId))
            {
                await StopConnectionAsync(existingId);
            }
        }
    }

    private async Task StartConnectionAsync(ChannelEntity channel, FeishuChannelSettings settings,
        CancellationToken ct)
    {
        string maskedSecret = ChannelConfigStore.MaskSecret(settings.AppSecret);
        _logger.LogInformation(
            "启动飞书 WebSocket 连接 channel={ChannelId} ({DisplayName}) appId={AppId} appSecret={MaskedSecret}",
            channel.Id, channel.DisplayName, settings.AppId, maskedSecret);

        if (string.IsNullOrWhiteSpace(settings.AppId) || string.IsNullOrWhiteSpace(settings.AppSecret))
        {
            _logger.LogError("飞书 WebSocket 启动失败：AppId 或 AppSecret 为空 channel={ChannelId}", channel.Id);
            return;
        }

        try
        {
            ServiceProvider sp = BuildChannelServiceProvider(channel, settings);

            // 获取 SDK 注册的 IHostedService（WssService）并启动
            IHostedService[] hostedServices = sp.GetServices<IHostedService>().ToArray();
            foreach (IHostedService svc in hostedServices)
            {
                await svc.StartAsync(ct);
            }

            // F-D-4: 每条连接启动专属监控任务，WssService 停止时立即触发指数退避重连
            CancellationTokenSource monitorCts = new();
            Task monitorTask = Task.Run(
                () => MonitorConnectionAsync(channel, settings, hostedServices, monitorCts.Token),
                CancellationToken.None);

            _connections[channel.Id] = new ChannelConnection(sp, hostedServices, monitorCts);

            _logger.LogInformation("飞书 WebSocket 已连接 channel={ChannelId}", channel.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "飞书 WebSocket 启动失败 channel={ChannelId}", channel.Id);
        }
    }

    /// <summary>
    /// F-D-4: 监控指定渠道的 WssService 生命周期。
    /// 若 WssService 意外停止（非主动关闭），则使用指数退避（5s→10s→20s→40s→60s 封顶）立即触发重连。
    /// </summary>
    private async Task MonitorConnectionAsync(
        ChannelEntity channel, FeishuChannelSettings settings,
        IHostedService[] hostedServices, CancellationToken monitorCt)
    {
        // 找到 SDK 的 WssService（BackgroundService），监听其 ExecuteTask
        Task? wssTask = hostedServices
            .OfType<BackgroundService>()
            .Select(bs => bs.ExecuteTask)
            .FirstOrDefault(t => t is not null);

        if (wssTask is not null)
        {
            try { await wssTask.WaitAsync(monitorCt); }
            catch (OperationCanceledException) { return; } // 主动停止，退出监控
            catch (Exception ex)
            {
                // WssService 自身抛异常（真实故障），记录后继续走重连逻辑
                _logger.LogError(ex, "WssService 异常退出 channel={ChannelId}", channel.Id);
            }

            // ExecuteTask 正常完成：SDK（WatsonWsClient）在内部线程维持连接，
            // 不代表断线，在此等待主动停止即可，由 SDK 自行负责重连。
            if (wssTask.IsCompletedSuccessfully)
            {
                try { await Task.Delay(Timeout.Infinite, monitorCt); }
                catch (OperationCanceledException) { }
                return;
            }
        }
        else
        {
            // 无法取得 ExecuteTask → 回退到轮询，每 5 秒检查一次连接是否仍在字典中
            try { await Task.Delay(Timeout.Infinite, monitorCt); }
            catch (OperationCanceledException) { return; }
        }

        if (monitorCt.IsCancellationRequested) return; // 主动停止期间不触发重连

        _logger.LogWarning("飞书 WebSocket 断线 channel={ChannelId}，准备重连", channel.Id);

        // 从字典移除旧连接（避免 SyncChannels 认为连接正常）
        if (_connections.TryRemove(channel.Id, out ChannelConnection? old))
        {
            old.MonitorCts.Cancel();
            try
            {
                foreach (IHostedService svc in old.HostedServices)
                    await svc.StopAsync(CancellationToken.None);
                await old.ServiceProvider.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "释放断线连接资源异常 channel={ChannelId}", channel.Id);
            }
        }

        // 指数退避重连：5s → 10s → 20s → 40s → 60s（封顶），最多不限次
        int delaySeconds = 5;
        while (!monitorCt.IsCancellationRequested)
        {
            _logger.LogInformation("飞书 WebSocket 重连等待 {Delay}s channel={ChannelId}", delaySeconds, channel.Id);
            try { await Task.Delay(TimeSpan.FromSeconds(delaySeconds), monitorCt); }
            catch (OperationCanceledException) { return; }

            delaySeconds = Math.Min(delaySeconds * 2, 60);

            // 如果配置已变更（禁用或切换模式），不再重连
            ChannelEntity? current = channelStore.GetByType(ChannelType.Feishu)
                .FirstOrDefault(c => c.Id == channel.Id);
            if (current is null || !current.IsEnabled) return;
            FeishuChannelSettings? currentSettings = FeishuChannelSettings.TryParse(current.SettingJson);
            if (currentSettings is null ||
                !string.Equals(currentSettings.ConnectionMode, "websocket", StringComparison.OrdinalIgnoreCase))
                return;

            // 已由别的路径重连成功，退出
            if (_connections.ContainsKey(channel.Id)) return;

            try
            {
                await StartConnectionAsync(current, currentSettings, monitorCt);
                _logger.LogInformation("飞书 WebSocket 重连成功 channel={ChannelId}", channel.Id);
                return; // StartConnectionAsync 内部已启动新的 MonitorTask，本 Task 退出
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "飞书 WebSocket 重连失败 channel={ChannelId}，{Delay}s 后重试",
                    channel.Id, delaySeconds);
            }
        }
    }

    /// <summary>构建渠道独立 ServiceProvider，包含 SDK、WebSocket、事件处理器。</summary>
    private ServiceProvider BuildChannelServiceProvider(ChannelEntity channel, FeishuChannelSettings settings)
    {
        ServiceCollection services = new();

        // F-E-1: 支持配置化 API Base URL
        Action<HttpClient>? configureHttpClient = null;
        if (!string.IsNullOrWhiteSpace(settings.ApiBaseUrl)
            && !settings.ApiBaseUrl.Equals("https://open.feishu.cn", StringComparison.OrdinalIgnoreCase))
        {
            string baseUrl = settings.ApiBaseUrl.TrimEnd('/');
            configureHttpClient = client => client.BaseAddress = new Uri(baseUrl);
        }

        // 注册 SDK
        services.AddFeishuNetSdk(
            appId: settings.AppId,
            appSecret: settings.AppSecret,
            encryptKey: settings.EncryptKey,
            verificationToken: settings.VerificationToken,
            httpClientOptions: configureHttpClient);

        // 注册 WebSocket 长连接
        services.AddFeishuWebSocket();

        // 注册渠道上下文（每个子容器独立）
        services.AddSingleton(new FeishuChannelContext(channel, settings));

        // 共享主容器的 Processor（其中需要 ProviderConfigStore 和 ProviderClientFactory）
        services.AddSingleton(providerStore);
        services.AddSingleton(clientFactory);
        services.AddSingleton(sessionService);
        // F-D-3: 共享 Token 缓存（子容器的 FeishuMessageProcessor 也能复用 Token）
        if (tokenCache is not null) services.AddSingleton(tokenCache);
        // 共享主容器的 Agent 消息处理器，使子容器的 FeishuMessageProcessor 能路由到 Agent（含工具）
        if (agentHandler is not null) services.AddSingleton(agentHandler);
        services.AddSingleton<FeishuMessageProcessor>();

        // 共享主容器的日志工厂
        services.AddSingleton(loggerFactory);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

        // 注册事件处理器（SDK 通过反射发现）
        services.AddScoped<IEventHandler<EventV2Dto<FeishuNetSdk.Im.Events.ImMessageReceiveV1EventBodyDto>, FeishuNetSdk.Im.Events.ImMessageReceiveV1EventBodyDto>, FeishuMessageEventHandler>();

        return services.BuildServiceProvider();
    }

    private async Task StopConnectionAsync(string channelId)
    {
        if (!_connections.TryRemove(channelId, out ChannelConnection? conn)) return;

        _logger.LogInformation("停止飞书 WebSocket 连接 channel={ChannelId}", channelId);

        // F-D-4: 先取消监控任务，防止 StopAsync 期间触发重连
        conn.MonitorCts.Cancel();

        try
        {
            foreach (IHostedService svc in conn.HostedServices)
            {
                await svc.StopAsync(CancellationToken.None);
            }

            await conn.ServiceProvider.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "飞书 WebSocket 停止异常 channel={ChannelId}", channelId);
        }
        finally
        {
            conn.MonitorCts.Dispose();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (string id in _connections.Keys.ToArray())
        {
            await StopConnectionAsync(id);
        }

        await base.StopAsync(cancellationToken);
    }

    /// <summary>
    /// F-F-2: 返回指定渠道 ID 的 WebSocket 连接状态。
    /// </summary>
    public string GetConnectionStatus(string channelId)
        => _connections.ContainsKey(channelId) ? "connected" : "disconnected";

    private sealed record ChannelConnection(
        ServiceProvider ServiceProvider,
        IHostedService[] HostedServices,
        CancellationTokenSource MonitorCts);
}
