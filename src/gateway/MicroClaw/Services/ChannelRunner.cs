using MicroClaw.Abstractions.Channel;
using MicroClaw.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Services;

/// <summary>
/// Background runner that drives <see cref="IChannelProvider"/> lifecycle hooks.
/// Calls <c>StartAsync</c> on each provider at startup, <c>TickAsync</c> every 30 seconds,
/// and <c>StopAsync</c> on shutdown.
/// </summary>
internal sealed class ChannelRunner(
    ChannelService channelService,
    ILoggerFactory loggerFactory) : BackgroundService
{
    private readonly ILogger<ChannelRunner> _logger = loggerFactory.CreateLogger<ChannelRunner>();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Start all providers
        foreach (IChannelProvider provider in channelService.GetProviders())
        {
            try
            {
                await provider.StartAsync(stoppingToken);
                _logger.LogInformation("渠道 Provider {Name} 已启动", provider.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "渠道 Provider {Name} 启动失败", provider.Name);
            }
        }

        // Periodic tick loop (30s interval)
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            foreach (IChannelProvider provider in channelService.GetProviders())
            {
                try
                {
                    await provider.TickAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "渠道 Provider {Name} Tick 异常", provider.Name);
                }
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (IChannelProvider provider in channelService.GetProviders())
        {
            try
            {
                await provider.StopAsync(cancellationToken);
                _logger.LogInformation("渠道 Provider {Name} 已停止", provider.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "渠道 Provider {Name} 停止异常", provider.Name);
            }
        }

        await base.StopAsync(cancellationToken);
    }
}
