using MicroClaw.Abstractions.Sessions;
using MicroClaw.Jobs;
using MicroClaw.Pet.Heartbeat;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet;

/// <summary>
/// P-G-2: Pet 心跳定时 Job。
/// <para>
/// 每 5 分钟（默认，可配）扫描所有活跃 Session 中已启用 Pet 的会话，
/// 并行调用 <see cref="PetHeartbeatExecutor"/> 执行心跳逻辑。
/// 单个 Session 的心跳失败不影响其他 Session。
/// </para>
/// </summary>
public sealed class PetHeartbeatJob(
    ISessionRepository sessionRepo,
    PetHeartbeatExecutor heartbeatExecutor,
    ILogger<PetHeartbeatJob> logger) : IScheduledJob
{
    /// <summary>并行心跳的最大并发数。</summary>
    internal const int MaxConcurrency = 5;

    public string JobName => "pet-heartbeat";

    public JobSchedule Schedule => new JobSchedule.FixedInterval(
        Interval: TimeSpan.FromMinutes(5),
        StartupDelay: TimeSpan.FromMinutes(2));

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var sessions = sessionRepo.GetAll();
        var candidates = new List<string>();

        // 筛选已审批的 Session（Pet 是否启用由 HeartbeatExecutor 内部检查）
        foreach (var session in sessions)
        {
            if (ct.IsCancellationRequested) break;
            if (!session.IsApproved) continue;
            // 若已有 Pet 且明确禁用，提前过滤，避免创建无效任务
            if (session.Pet is { IsEnabled: false }) continue;
            candidates.Add(session.Id);
        }

        if (candidates.Count == 0)
        {
            logger.LogDebug("PetHeartbeatJob: 无活跃 Session，跳过");
            return;
        }

        logger.LogDebug("PetHeartbeatJob: 开始心跳，候选 Session {Count} 个", candidates.Count);

        int executed = 0;
        int skipped = 0;
        int failed = 0;

        // 使用 SemaphoreSlim 控制并发
        using var semaphore = new SemaphoreSlim(MaxConcurrency);
        var tasks = candidates.Select(async sessionId =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var result = await heartbeatExecutor.ExecuteAsync(sessionId, ct);
                if (!result.Executed) Interlocked.Increment(ref skipped);
                else if (result.IsSuccess) Interlocked.Increment(ref executed);
                else Interlocked.Increment(ref failed);
            }
            catch (OperationCanceledException) { /* propagate via ct */ }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                logger.LogWarning(ex, "PetHeartbeatJob: Session [{SessionId}] 心跳异常", sessionId);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        if (executed > 0 || failed > 0)
        {
            logger.LogInformation(
                "PetHeartbeatJob 完成: 执行={Executed}, 跳过={Skipped}, 失败={Failed}",
                executed, skipped, failed);
        }
    }
}
