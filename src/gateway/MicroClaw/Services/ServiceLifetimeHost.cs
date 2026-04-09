using MicroClaw.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Services;

/// <summary>
/// 统一生命周期宿主。
/// <para>
/// <see cref="IHostedService"/> 中最先注册，负责按 <see cref="IService.InitOrder"/> 升序调用所有
/// <see cref="IService.InitializeAsync"/>，并在停止时以降序调用 <see cref="IService.DisposeAsync"/>。
/// </para>
/// </summary>
public sealed class ServiceLifetimeHost : IHostedService
{
    private readonly IReadOnlyList<IService> _services;
    private readonly ILogger<ServiceLifetimeHost> _logger;

    public ServiceLifetimeHost(IEnumerable<IService> services, ILogger<ServiceLifetimeHost> logger)
    {
        _services = [.. services.OrderBy(s => s.InitOrder)];
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var service in _services)
        {
            _logger.LogInformation("初始化服务 {Service}（InitOrder={Order}）...", service.GetType().Name, service.InitOrder);
            await service.InitializeAsync(cancellationToken);
            _logger.LogInformation("服务 {Service} 初始化完成。", service.GetType().Name);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var service in ((IEnumerable<IService>)_services).Reverse())
        {
            try
            {
                await service.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "服务 {Service} 销毁时出现异常。", service.GetType().Name);
            }
        }
    }
}
