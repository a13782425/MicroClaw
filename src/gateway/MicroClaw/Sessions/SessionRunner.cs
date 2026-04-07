using System.Diagnostics;
using MicroClaw.Abstractions.Sessions;

namespace MicroClaw.Sessions;

/// <summary>
/// Session 生命周期驱动器（占位实现）。
/// <para>
/// 作为 <see cref="BackgroundService"/> 注册到 DI，
/// 将来用于驱动 Session 的定时 Update（如 Pet 状态推进、心跳检测等）。
/// 当前为空实现，等待后续功能填充。
/// </para>
/// </summary>
public sealed class SessionRunner(
    ISessionService _,
    ILogger<SessionRunner> logger) : BackgroundService
{
    private readonly TimeSpan _tickInterval = TimeSpan.FromMilliseconds(100); // 10 FPS
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var start = Stopwatch.GetTimestamp();
            
            Update(); // 你的 Tick 逻辑
            
            var elapsed = Stopwatch.GetElapsedTime(start);
            var delay = _tickInterval - elapsed;
            
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);
        }
    }
    private void Update()
    {
        logger.LogInformation("SessionRunner Tick at {Time}", DateTimeOffset.Now);
    }
}
