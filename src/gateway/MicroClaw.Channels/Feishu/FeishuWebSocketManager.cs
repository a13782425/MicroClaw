using System.Collections.Concurrent;
using FeishuNetSdk.Services;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
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
    IChannelSessionService sessionService,
    ILoggerFactory loggerFactory) : BackgroundService
{
    private readonly ILogger<FeishuWebSocketManager> _logger = loggerFactory.CreateLogger<FeishuWebSocketManager>();
    private readonly ConcurrentDictionary<string, ChannelConnection> _connections = new();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // 启动时同步一次
        await SyncChannelsAsync(stoppingToken);

        // 定期轮询配置变更
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
                await SyncChannelsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "飞书 WebSocket 配置同步异常");
            }
        }
    }

    /// <summary>对比当前连接与数据库配置，启停对应渠道的 WebSocket 连接。</summary>
    private async Task SyncChannelsAsync(CancellationToken ct)
    {
        IReadOnlyList<ChannelConfig> feishuChannels = channelStore.GetByType(ChannelType.Feishu);

        // 需要活跃的渠道 ID 集合
        HashSet<string> desiredIds = new(StringComparer.OrdinalIgnoreCase);

        foreach (ChannelConfig channel in feishuChannels)
        {
            if (!channel.IsEnabled) continue;

            FeishuChannelSettings? settings = FeishuChannelSettings.TryParse(channel.SettingsJson);
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

    private async Task StartConnectionAsync(ChannelConfig channel, FeishuChannelSettings settings,
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
            // 构建独立 ServiceProvider，包含 SDK + WebSocket + 事件处理器
            ServiceCollection services = new();

            // 注册 SDK
            services.AddFeishuNetSdk(
                appId: settings.AppId,
                appSecret: settings.AppSecret,
                encryptKey: settings.EncryptKey,
                verificationToken: settings.VerificationToken);

            // 注册 WebSocket 长连接
            services.AddFeishuWebSocket();

            // 注册渠道上下文（每个子容器独立）
            services.AddSingleton(new FeishuChannelContext(channel, settings));

            // 共享主容器的 Processor（其中需要 ProviderConfigStore 和 ProviderClientFactory）
            services.AddSingleton(providerStore);
            services.AddSingleton(clientFactory);
            services.AddSingleton(sessionService);
            services.AddSingleton<FeishuMessageProcessor>();

            // 共享主容器的日志工厂
            services.AddSingleton(loggerFactory);
            services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

            // 注册事件处理器（SDK 通过反射发现）
            services.AddScoped<IEventHandler<EventV2Dto<FeishuNetSdk.Im.Events.ImMessageReceiveV1EventBodyDto>, FeishuNetSdk.Im.Events.ImMessageReceiveV1EventBodyDto>, FeishuMessageEventHandler>();

            ServiceProvider sp = services.BuildServiceProvider();

            // 获取 SDK 注册的 IHostedService（WssService）并启动
            IEnumerable<IHostedService> hostedServices = sp.GetServices<IHostedService>();
            foreach (IHostedService svc in hostedServices)
            {
                await svc.StartAsync(ct);
            }

            _connections[channel.Id] = new ChannelConnection(sp, hostedServices.ToArray());

            _logger.LogInformation("飞书 WebSocket 已连接 channel={ChannelId}", channel.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "飞书 WebSocket 启动失败 channel={ChannelId}", channel.Id);
        }
    }

    private async Task StopConnectionAsync(string channelId)
    {
        if (!_connections.TryRemove(channelId, out ChannelConnection? conn)) return;

        _logger.LogInformation("停止飞书 WebSocket 连接 channel={ChannelId}", channelId);

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
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (string id in _connections.Keys.ToArray())
        {
            await StopConnectionAsync(id);
        }

        await base.StopAsync(cancellationToken);
    }

    private sealed record ChannelConnection(ServiceProvider ServiceProvider, IHostedService[] HostedServices);
}
