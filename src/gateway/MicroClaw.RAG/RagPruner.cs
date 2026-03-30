using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroClaw.RAG;

/// <summary>
/// Interface for pruning RAG storage when it exceeds configured size limits.
/// </summary>
public interface IRagPruner
{
    /// <summary>
    /// Check if the RAG DB for the given scope exceeds the max size, and prune low-HitCount chunks if so.
    /// </summary>
    Task PruneIfNeededAsync(RagScope scope, string? sessionId, CancellationToken ct = default);
}

/// <summary>
/// Prunes RAG vector chunks when the database file exceeds the configured storage limit.
/// Deletes chunks with the lowest HitCount first (oldest first as tiebreaker).
/// Uses PRAGMA incremental_vacuum instead of full VACUUM to avoid locking the database.
/// </summary>
public sealed class RagPruner : IRagPruner
{
    private readonly RagDbContextFactory _dbFactory;
    private readonly ILogger<RagPruner> _logger;
    private double _maxStorageSizeMb;
    private double _pruneTargetPercent;

    public RagPruner(
        RagDbContextFactory dbFactory,
        ILogger<RagPruner> logger,
        double maxStorageSizeMb = 50,
        double pruneTargetPercent = 0.8)
    {
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxStorageSizeMb = maxStorageSizeMb;
        _pruneTargetPercent = Math.Clamp(pruneTargetPercent, 0.1, 1.0);
    }

    /// <summary>Update thresholds at runtime (called when config is changed via API).</summary>
    public void UpdateThresholds(double maxStorageSizeMb, double pruneTargetPercent)
    {
        _maxStorageSizeMb = maxStorageSizeMb;
        _pruneTargetPercent = Math.Clamp(pruneTargetPercent, 0.1, 1.0);
    }

    public async Task PruneIfNeededAsync(RagScope scope, string? sessionId, CancellationToken ct = default)
    {
        string dbPath = _dbFactory.ResolveDatabasePath(scope, sessionId);

        if (!File.Exists(dbPath)) return;

        long fileSizeBytes = new FileInfo(dbPath).Length;
        double fileSizeMb = fileSizeBytes / (1024.0 * 1024.0);

        if (fileSizeMb <= _maxStorageSizeMb) return;

        double targetSizeMb = _maxStorageSizeMb * _pruneTargetPercent;
        _logger.LogInformation(
            "RagPruner: {Scope}/{SessionId} 存储 {CurrentMb:F2}MB 超过阈值 {MaxMb:F2}MB，开始清理到 {TargetMb:F2}MB",
            scope, sessionId ?? "global", fileSizeMb, _maxStorageSizeMb, targetSizeMb);

        int totalDeleted = 0;

        // Delete in batches of 100 to avoid holding large transactions
        const int batchSize = 100;

        while (!ct.IsCancellationRequested)
        {
            // Re-check file size after each batch
            fileSizeBytes = new FileInfo(dbPath).Length;
            fileSizeMb = fileSizeBytes / (1024.0 * 1024.0);

            if (fileSizeMb <= targetSizeMb) break;

            using var db = _dbFactory.Create(scope, sessionId);

            // Get the lowest-priority chunks: lowest HitCount, then oldest
            var candidates = await db.VectorChunks
                .OrderBy(c => c.HitCount)
                .ThenBy(c => c.CreatedAtMs)
                .Take(batchSize)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (candidates.Count == 0) break;

            db.VectorChunks.RemoveRange(candidates);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            totalDeleted += candidates.Count;

            // Use incremental_vacuum to release freed pages back to the OS
            // This requires auto_vacuum=INCREMENTAL, so enable it first
            await db.Database.ExecuteSqlRawAsync("PRAGMA auto_vacuum = INCREMENTAL", ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("PRAGMA incremental_vacuum", ct).ConfigureAwait(false);
        }

        if (totalDeleted > 0)
        {
            double finalSizeMb = new FileInfo(dbPath).Length / (1024.0 * 1024.0);
            _logger.LogInformation(
                "RagPruner: {Scope}/{SessionId} 清理完成，删除 {Count} 个 chunks，存储 {BeforeMb:F2}MB → {AfterMb:F2}MB",
                scope, sessionId ?? "global", totalDeleted, fileSizeMb, finalSizeMb);
        }
    }
}
