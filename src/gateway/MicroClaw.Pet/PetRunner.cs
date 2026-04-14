using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet;

/// <summary>
/// Pet 状态轮转驱动器（占位实现）。
/// <para>
/// 作为 <see cref="BackgroundService"/> 注册到 DI，
/// 未来用于驱动 Pet 状态机轮转（PetStateMachine.EvaluateAsync）、
/// 心跳执行（PetHeartbeatExecutor）和情绪衰减等周期性任务。
/// 当前为空实现，等待后续功能填充。
/// </para>
/// </summary>
public sealed class PetRunner : BackgroundService
{
    private readonly IServiceProvider _sp;
    private readonly ILogger<PetRunner> _logger;

    public PetRunner(IServiceProvider sp)
    {
        _sp = sp;
        _logger = sp.GetRequiredService<ILogger<PetRunner>>();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // TODO: 状态轮转循环（PetStateMachine + PetHeartbeatExecutor 整合）
        return Task.CompletedTask;
    }
}
