using Microsoft.Extensions.Logging;

namespace MicroClaw.RAG;

/// <summary>
/// 全量重索引服务：切换嵌入模型后，将维度不匹配的向量分块重新嵌入。
/// TODO: Rewrite to accept MicroRag instances instead of IRagService/RagDbContextFactory.
/// </summary>
public sealed class RagReindexService(
    IEmbeddingService embedding,
    ILogger<RagReindexService> logger)
{
    public Task RunAsync(RagReindexJobTracker tracker, CancellationToken ct = default)
    {
        // TODO: Reimplement using MicroRag instances
        logger.LogWarning("RagReindexService is temporarily stubbed during MicroRag migration");
        tracker.Start(0);
        tracker.Complete();
        return Task.CompletedTask;
    }
}
