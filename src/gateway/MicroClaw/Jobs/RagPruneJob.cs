using MicroClaw.Pet.Rag;
using MicroClaw.RAG;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Jobs;

/// <summary>
/// 2-A-11: RAG 定期容量清理 Job。
/// 每天凌晨 1 点（UTC）执行，对全局 RAG 库和所有已知会话 RAG 库调用 IRagPruner.PruneIfNeededAsync，
/// 补充仅在 ingest 时 fire-and-forget 的临时剪枝，确保长期运行后存储不会无限增长。
/// 同时覆盖 Pet 私有 RAG 库（<c>{sessionId}/pet/knowledge.db</c>）的清理。
/// </summary>
public sealed class RagPruneJob(
    IRagPruner pruner,
    RagDbContextFactory dbFactory,
    PetRagScope? petRagScope,
    ILogger<RagPruneJob> logger) : IScheduledJob
{
    // 每天凌晨 1 点（UTC）执行，早于 MemorySummarizationJob（02:00）和 DreamingJob（03:00）
    internal static readonly TimeOnly RunTime = new(1, 0, 0);

    public string JobName => "rag-prune";

    public JobSchedule Schedule => new JobSchedule.DailyAt(RunTime, TimeSpan.FromMinutes(2));

    public async Task ExecuteAsync(CancellationToken ct)
    {
        // 1. 全局库
        await PruneScopeAsync(RagScope.Global, null, ct);

        // 2. 所有已存在的会话库
        IReadOnlyList<string> sessionIds = dbFactory.GetAllSessionIds();
        foreach (string sessionId in sessionIds)
        {
            if (ct.IsCancellationRequested) break;
            await PruneScopeAsync(RagScope.Session, sessionId, ct);
        }

        // 3. 所有 Pet 私有 RAG 库
        int petCount = 0;
        if (petRagScope is not null)
        {
            IReadOnlyList<string> petSessionIds = petRagScope.GetAllPetSessionIds();
            foreach (string sessionId in petSessionIds)
            {
                if (ct.IsCancellationRequested) break;
                try
                {
                    await petRagScope.PruneIfNeededAsync(sessionId, ct: ct);
                    petCount++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    logger.LogError(ex, "RagPruneJob: 清理 Pet RAG/{SessionId} 失败", sessionId);
                }
            }
        }

        logger.LogInformation("RagPruneJob: 完成，共扫描 {Count} 个会话库 + {PetCount} 个 Pet RAG 库",
            sessionIds.Count, petCount);
    }

    private async Task PruneScopeAsync(RagScope scope, string? sessionId, CancellationToken ct)
    {
        try
        {
            await pruner.PruneIfNeededAsync(scope, sessionId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 单个库失败不中断整体流程
            logger.LogError(ex, "RagPruneJob: 清理 {Scope}/{SessionId} 失败",
                scope, sessionId ?? "global");
        }
    }
}
