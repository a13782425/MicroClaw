using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Jobs;

/// <summary>
/// RAG 定期容量清理 Job — TODO: Reimplement with MicroRag.
/// </summary>
public sealed class RagPruneJob : IScheduledJob
{
    private readonly ILogger<RagPruneJob> _logger;

    public RagPruneJob(IServiceProvider sp)
    {
        _logger = sp.GetRequiredService<ILogger<RagPruneJob>>();
    }

    internal static readonly TimeOnly RunTime = new(1, 0, 0);

    public string JobName => "rag-prune";

    public JobSchedule Schedule => new JobSchedule.DailyAt(RunTime, TimeSpan.FromMinutes(2));

    public Task ExecuteAsync(CancellationToken ct)
    {
        // Temporarily disabled during MicroRag migration
        _logger.LogInformation("RagPruneJob: 跳过（MicroRag 迁移中）");
        return Task.CompletedTask;
    }
}
