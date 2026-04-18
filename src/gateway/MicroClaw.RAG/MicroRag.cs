using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MicroClaw.Abstractions;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Core;
using MicroClaw.Core.Logging;
using MicroClaw.Providers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.RAG;

/// <summary>
/// Self-contained RAG object: <c>new MicroRag(sp, dbPath)</c> gives full RAG capabilities.
/// Inherits <see cref="MicroObject"/> for lifecycle and component support.
/// <para>
/// Replaces the old <c>IRagService</c>, <c>HybridSearchService</c>, <c>RagPruner</c>,
/// <c>RagDbContextFactory</c>, and <c>PetRagScope</c> — all inlined into this single class.
/// </para>
/// </summary>
public sealed class MicroRag : MicroObject
{
    private readonly ProviderService _providerService;
    private readonly ConcurrentDictionary<string, bool> _initialized = new(StringComparer.OrdinalIgnoreCase);

    private double _maxStorageSizeMb;
    private double _pruneTargetPercent;

    /// <summary>Absolute path to the SQLite database file.</summary>
    public string DbPath { get; }

    /// <summary>Parent RAGs whose results are merged during queries (e.g. global RAG for session RAG).</summary>
    public IReadOnlyList<MicroRag> Parents { get; }

    /// <summary>Optional document storage directory (typically only set on the global RAG).</summary>
    public string? DocsPath { get; init; }

    /// <summary>
    /// Create a new self-contained RAG instance.
    /// </summary>
    /// <param name="sp">Service provider used to resolve <see cref="ProviderService"/>.</param>
    /// <param name="dbPath">Absolute path to the SQLite RAG database file.</param>
    /// <param name="parents">Optional parent RAGs whose results merge into queries.</param>
    public MicroRag(IServiceProvider sp, string dbPath, IReadOnlyList<MicroRag>? parents = null)
    {
        ArgumentNullException.ThrowIfNull(sp);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

        DbPath = dbPath;
        Parents = parents ?? [];
        _providerService = sp.GetRequiredService<ProviderService>();

        var opts = MicroClawConfig.Get<RagOptions>();
        _maxStorageSizeMb = opts.MaxStorageSizeMb;
        _pruneTargetPercent = Math.Clamp(opts.PruneTargetPercent, 0.1, 1.0);
    }

    // ── Embedding bridge ──────────────────────────────────────────────────
    //
    // Uses the default <see cref="EmbeddingMicroProvider"/> resolved from
    // <see cref="ProviderService"/>. RAG is session-less here, so we tag usage
    // with a fixed <c>"rag"</c> sessionId via <see cref="MicroChatContext.ForSystem(string,string,CancellationToken)"/>.
    // TODO: 当 MicroChatContext 能传递真实 Session 时，Ingest/Query 走外部 ctx 归属。

    private async Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedBatchInternalAsync(
        IEnumerable<string> inputs, CancellationToken ct)
    {
        var embedProvider = _providerService.GetDefaultEmbeddingProvider()
            ?? throw new InvalidOperationException("No default embedding provider is configured.");
        var ctx = MicroChatContext.ForSystem("rag", "rag-embed", ct);
        var materialized = inputs as IReadOnlyList<string> ?? inputs.ToList();
        var batch = await embedProvider.EmbedBatchAsync(ctx, materialized).ConfigureAwait(false);
        var result = new ReadOnlyMemory<float>[batch.Count];
        for (int i = 0; i < batch.Count; i++)
            result[i] = batch[i].Vector;
        return result;
    }

    private async Task<ReadOnlyMemory<float>> EmbedInternalAsync(string text, CancellationToken ct)
    {
        var batch = await EmbedBatchInternalAsync([text], ct).ConfigureAwait(false);
        return batch[0];
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Ingest
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Ingest text into the local RAG DB with an auto-generated sourceId.</summary>
    public async Task IngestAsync(string source, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source)) return;

        var chunks = ChunkText(source);
        if (chunks.Count == 0) return;

        var vectors = await EmbedBatchInternalAsync(chunks.Select(c => c.Content), ct)
            .ConfigureAwait(false);

        var sourceId = Guid.NewGuid().ToString("N");
        var createdAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var entities = BuildEntities(chunks, vectors, sourceId, createdAtMs);

        using var db = CreateDb();
        db.VectorChunks.AddRange(entities);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        FirePruneIfNeeded();
    }

    /// <summary>Ingest text with a fixed sourceId (idempotent — skips if sourceId already exists).</summary>
    public async Task IngestAsync(string source, string sourceId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        using var existCheck = CreateDb();
        if (await existCheck.VectorChunks.AsNoTracking().AnyAsync(e => e.SourceId == sourceId, ct).ConfigureAwait(false))
            return;

        var chunks = ChunkText(source);
        if (chunks.Count == 0) return;

        var vectors = await EmbedBatchInternalAsync(chunks.Select(c => c.Content), ct)
            .ConfigureAwait(false);

        var createdAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var entities = BuildEntities(chunks, vectors, sourceId, createdAtMs);

        using var db = CreateDb();
        db.VectorChunks.AddRange(entities);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        FirePruneIfNeeded();
    }

    /// <summary>Delete existing chunks for sourceId then ingest new content (atomic upsert).</summary>
    public async Task UpsertSourceAsync(string content, string sourceId, CancellationToken ct = default)
    {
        await DeleteBySourceIdAsync(sourceId, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(content))
        {
            var chunks = ChunkText(content);
            if (chunks.Count == 0) return;

            var vectors = await EmbedBatchInternalAsync(chunks.Select(c => c.Content), ct)
                .ConfigureAwait(false);

            var createdAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var entities = BuildEntities(chunks, vectors, sourceId, createdAtMs);

            using var db = CreateDb();
            db.VectorChunks.AddRange(entities);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            FirePruneIfNeeded();
        }
    }

    /// <summary>
    /// Ingest a document file. Uses <c>doc:{fileName}</c> as sourceId; replaces existing chunks if same name.
    /// </summary>
    /// <returns>The generated sourceId.</returns>
    public async Task<string> IngestDocumentAsync(string source, string fileName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var sourceId = $"doc:{fileName}";

        await DeleteBySourceIdAsync(sourceId, ct).ConfigureAwait(false);

        var chunks = ChunkText(source);
        if (chunks.Count == 0) return sourceId;

        var vectors = await EmbedBatchInternalAsync(chunks.Select(c => c.Content), ct)
            .ConfigureAwait(false);

        var createdAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var entities = chunks.Select((chunk, i) => new VectorChunkEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceId = sourceId,
            Content = chunk.Content,
            VectorBlob = VectorHelper.ToBytes(vectors[i].Span),
            MetadataJson = JsonSerializer.Serialize(
                new { chunkIndex = chunk.Index, tokenCount = chunk.TokenCount, filename = fileName }),
            CreatedAtMs = createdAtMs,
        }).ToList();

        using var db = CreateDb();
        db.VectorChunks.AddRange(entities);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        FirePruneIfNeeded();

        return sourceId;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Query (with parent merge)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Query this RAG + all parents, returning merged and ranked text results.
    /// </summary>
    public async Task<string> QueryAsync(string query, int topK = 10, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var options = new HybridSearchOptions { TopK = topK };
        var sw = Stopwatch.StartNew();

        var merged = await SearchMergedAsync(query, options, ct).ConfigureAwait(false);

        sw.Stop();
        RecordStatFireAndForget(sw.ElapsedMilliseconds, merged.Count);

        if (merged.Count == 0) return string.Empty;

        return string.Join("\n---\n", merged.Select(r => r.Record.Content));
    }

    /// <summary>
    /// Query with structured metadata (chunk refs for auditing).
    /// Merges results from this RAG + all parents.
    /// </summary>
    public async Task<RagQueryResult> QueryWithMetadataAsync(string query, int topK = 10, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var options = new HybridSearchOptions { TopK = topK };
        var sw = Stopwatch.StartNew();

        var merged = await SearchMergedAsync(query, options, ct).ConfigureAwait(false);

        sw.Stop();
        RecordStatFireAndForget(sw.ElapsedMilliseconds, merged.Count);

        if (merged.Count == 0)
            return new RagQueryResult(string.Empty, []);

        // Build chunk refs — Scope/SessionId kept for backward compat, will be removed later
        var chunkRefs = merged.Select(r => new RagChunkRef(
            r.Record.Id,
            r.Record.Content,
            r.Record.HitCount,
            r.Record.SourceId,
            RagScope.Session,
            null)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("以下是从知识库中检索到的相关片段，命中次数越高说明该知识被验证使用的次数越多，可信度越高：");
        sb.AppendLine();
        foreach (var chunk in chunkRefs)
        {
            sb.AppendLine($"[可信度: {chunk.HitCount} 次命中]");
            sb.AppendLine(chunk.Content);
            sb.AppendLine("---");
        }

        return new RagQueryResult(sb.ToString().TrimEnd(), chunkRefs);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  CRUD
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Delete all chunks with the given sourceId from the local DB.</summary>
    public async Task DeleteBySourceIdAsync(string sourceId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        using var db = CreateDb();
        var existing = await db.VectorChunks
            .Where(e => e.SourceId == sourceId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (existing.Count > 0)
        {
            db.VectorChunks.RemoveRange(existing);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Delete a single chunk by ID.</summary>
    public async Task DeleteChunkAsync(string chunkId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);

        using var db = CreateDb();
        var chunk = await db.VectorChunks
            .FirstOrDefaultAsync(e => e.Id == chunkId, ct)
            .ConfigureAwait(false);

        if (chunk is not null)
        {
            db.VectorChunks.Remove(chunk);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Get all distinct sourceIds in the local DB.</summary>
    public async Task<IReadOnlySet<string>> GetIndexedSourceIdsAsync(CancellationToken ct = default)
    {
        using var db = CreateDb();
        var ids = await db.VectorChunks
            .AsNoTracking()
            .Select(e => e.SourceId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>List all chunks ordered by HitCount desc.</summary>
    public async Task<IReadOnlyList<RagChunkInfo>> ListChunksAsync(CancellationToken ct = default)
    {
        using var db = CreateDb();
        var chunks = await db.VectorChunks
            .AsNoTracking()
            .OrderByDescending(e => e.HitCount)
            .ThenByDescending(e => e.CreatedAtMs)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return chunks.Select(e => new RagChunkInfo(
            e.Id, e.SourceId, e.Content,
            e.HitCount, e.CreatedAtMs, e.LastAccessedAtMs
        )).ToList();
    }

    /// <summary>List indexed documents (sourceId starting with "doc:").</summary>
    public async Task<IReadOnlyList<RagDocumentInfo>> ListDocumentsAsync(CancellationToken ct = default)
    {
        using var db = CreateDb();

        var chunks = await db.VectorChunks
            .AsNoTracking()
            .Where(e => e.SourceId.StartsWith("doc:"))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return chunks
            .GroupBy(e => e.SourceId)
            .Select(g =>
            {
                var first = g.OrderBy(e => e.CreatedAtMs).First();
                string fileName = TryParseFileName(first.MetadataJson) ?? g.Key["doc:".Length..];
                return new RagDocumentInfo(
                    SourceId: g.Key,
                    FileName: fileName,
                    ChunkCount: g.Count(),
                    IndexedAtMs: first.CreatedAtMs);
            })
            .OrderBy(d => d.FileName)
            .ToList();
    }

    /// <summary>Get total chunk count in the local DB.</summary>
    public async Task<int> GetChunkCountAsync(CancellationToken ct = default)
    {
        using var db = CreateDb();
        return await db.VectorChunks.CountAsync(ct).ConfigureAwait(false);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  HitCount
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Set HitCount for a specific chunk.</summary>
    public async Task UpdateChunkHitCountAsync(string chunkId, int hitCount, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);
        if (hitCount < 0) throw new ArgumentOutOfRangeException(nameof(hitCount), "HitCount must be >= 0.");

        using var db = CreateDb();
        var chunk = await db.VectorChunks
            .FirstOrDefaultAsync(e => e.Id == chunkId, ct)
            .ConfigureAwait(false);

        if (chunk is not null)
        {
            chunk.HitCount = hitCount;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Increment HitCount by 1 for the given chunk IDs.</summary>
    public async Task IncrementHitCountAsync(IReadOnlyList<string> chunkIds, CancellationToken ct = default)
    {
        if (chunkIds.Count == 0) return;

        using var db = CreateDb();
        var chunks = await db.VectorChunks
            .Where(e => chunkIds.Contains(e.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var chunk in chunks)
            chunk.HitCount += 1;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Stats
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Read this RAG's own stat row (no parent merge, no derived values).
    /// Returns zeros if no stat row has been written yet.
    /// </summary>
    internal async Task<(long TotalQueries, long HitQueries, long TotalElapsedMs, long TotalRecallCount)>
        ReadLocalStatAsync(CancellationToken ct = default)
    {
        using var db = CreateDb();
        var row = await db.SearchStats
            .AsNoTracking()
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        return row is null
            ? (0, 0, 0, 0)
            : (row.TotalQueries, row.HitQueries, row.TotalElapsedMs, row.TotalRecallCount);
    }

    /// <summary>
    /// Get aggregated query stats for this RAG, merged unidirectionally with all parents.
    /// <para>
    /// Only walks up to <see cref="Parents"/>; never descends to children. This mirrors the
    /// <c>SearchMergedAsync</c> pattern and guarantees global/root RAGs stay isolated from sessions.
    /// </para>
    /// </summary>
    public async Task<RagQueryStats> GetQueryStatsAsync(CancellationToken ct = default)
    {
        string scopeLabel = Path.GetFileNameWithoutExtension(DbPath);

        var localTask = ReadLocalStatAsync(ct);
        var parentTasks = Parents.Select(p => p.ReadLocalStatAsync(ct)).ToList();

        var allTasks = new List<Task<(long, long, long, long)>>(parentTasks.Count + 1) { localTask };
        allTasks.AddRange(parentTasks);
        await Task.WhenAll(allTasks).ConfigureAwait(false);

        long total = 0, hits = 0, elapsed = 0, recall = 0;
        foreach (var t in allTasks)
        {
            var (tq, hq, tem, trc) = t.Result;
            total += tq;
            hits += hq;
            elapsed += tem;
            recall += trc;
        }

        return RagQueryStats.Derive(scopeLabel, total, hits, elapsed, recall);
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Prune
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Update prune thresholds at runtime.</summary>
    public void UpdateThresholds(double maxStorageSizeMb, double pruneTargetPercent)
    {
        _maxStorageSizeMb = maxStorageSizeMb;
        _pruneTargetPercent = Math.Clamp(pruneTargetPercent, 0.1, 1.0);
    }

    /// <summary>Prune low-HitCount chunks when the DB exceeds the configured size limit.</summary>
    public async Task PruneIfNeededAsync(CancellationToken ct = default)
    {
        if (!File.Exists(DbPath)) return;

        long fileSizeBytes = new FileInfo(DbPath).Length;
        double fileSizeMb = fileSizeBytes / (1024.0 * 1024.0);

        if (fileSizeMb <= _maxStorageSizeMb) return;

        double targetSizeMb = _maxStorageSizeMb * _pruneTargetPercent;
        Logger.LogInformation(
            "MicroRag: {DbPath} 存储 {CurrentMb:F2}MB 超过阈值 {MaxMb:F2}MB，开始清理到 {TargetMb:F2}MB",
            DbPath, fileSizeMb, _maxStorageSizeMb, targetSizeMb);

        int totalDeleted = 0;
        const int batchSize = 100;

        while (!ct.IsCancellationRequested)
        {
            fileSizeBytes = new FileInfo(DbPath).Length;
            fileSizeMb = fileSizeBytes / (1024.0 * 1024.0);

            if (fileSizeMb <= targetSizeMb) break;

            using var db = CreateDb();

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

            await db.Database.ExecuteSqlRawAsync("PRAGMA auto_vacuum = INCREMENTAL", ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlRawAsync("PRAGMA incremental_vacuum", ct).ConfigureAwait(false);
        }

        if (totalDeleted > 0)
        {
            double finalSizeMb = new FileInfo(DbPath).Length / (1024.0 * 1024.0);
            Logger.LogInformation(
                "MicroRag: {DbPath} 清理完成，删除 {Count} 个 chunks，存储 {BeforeMb:F2}MB → {AfterMb:F2}MB",
                DbPath, totalDeleted, fileSizeMb, finalSizeMb);
        }
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Connection pool cleanup
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Clear the SQLite connection pool for this database (call before deleting DB file on Windows).</summary>
    public void CloseDatabase()
    {
        SqliteConnection.ClearPool(new SqliteConnection($"Data Source={DbPath}"));
    }

    protected override ValueTask OnDisposedAsync(CancellationToken cancellationToken)
    {
        CloseDatabase();
        return ValueTask.CompletedTask;
    }

    // ══════════════════════════════════════════════════════════════════════
    //  Hybrid Search (inlined from HybridSearchService)
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>Search the local DB only (no parent merge).</summary>
    internal async Task<IReadOnlyList<HybridSearchResult>> SearchLocalAsync(
        string query, HybridSearchOptions options, CancellationToken ct)
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var semanticTask = SemanticSearchAsync(query, options, ct);
        var keywordTask = KeywordSearchAsync(query, options, ct);
        await Task.WhenAll(semanticTask, keywordTask).ConfigureAwait(false);

        var results = Fuse(semanticTask.Result, keywordTask.Result, options, nowMs);

        if (results.Count > 0)
        {
            var ids = results.Select(r => r.Record.Id).ToList();
            _ = UpdateLastAccessedAsync(ids, nowMs);
        }

        return results;
    }

    private async Task<Dictionary<string, (VectorChunkEntity Entity, float Score)>> SemanticSearchAsync(
        string query, HybridSearchOptions options, CancellationToken ct)
    {
        ReadOnlyMemory<float> queryVec;
        try
        {
            queryVec = await EmbedInternalAsync(query, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogWarning(ex, "Embedding 向量化失败，语义检索将被跳过，降级为纯关键词检索");
            return new Dictionary<string, (VectorChunkEntity, float)>();
        }

        var candidateCount = options.TopK * options.SemanticCandidateMultiplier;

        using var db = CreateDb();
        var all = await db.VectorChunks.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        var scored = all
            .Where(e => e.VectorBlob.Length > 0)
            .Select(e => new
            {
                e,
                Score = VectorHelper.CosineSimilarity(queryVec.Span, VectorHelper.ToFloats(e.VectorBlob))
            })
            .OrderByDescending(x => x.Score)
            .Take(candidateCount);

        var dict = new Dictionary<string, (VectorChunkEntity, float)>();
        foreach (var item in scored)
            dict[item.e.Id] = (item.e, item.Score);

        return dict;
    }

    private async Task<Dictionary<string, (VectorChunkEntity Entity, float Score)>> KeywordSearchAsync(
        string query, HybridSearchOptions options, CancellationToken ct)
    {
        var keywords = Tokenize(query);
        if (keywords.Count == 0)
            return new Dictionary<string, (VectorChunkEntity, float)>();

        using var db = CreateDb();
        var all = await db.VectorChunks.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        var dict = new Dictionary<string, (VectorChunkEntity, float)>();
        foreach (var entity in all)
        {
            var contentLower = entity.Content.ToLowerInvariant();
            int hits = keywords.Count(kw => contentLower.Contains(kw, StringComparison.Ordinal));
            if (hits == 0) continue;

            float score = (float)hits / keywords.Count;
            dict[entity.Id] = (entity, score);
        }

        return dict;
    }

    /// <summary>Weighted fusion of semantic + keyword results with optional time decay.</summary>
    internal static IReadOnlyList<HybridSearchResult> Fuse(
        Dictionary<string, (VectorChunkEntity Entity, float Score)> semantic,
        Dictionary<string, (VectorChunkEntity Entity, float Score)> keyword,
        HybridSearchOptions options,
        long nowMs = 0)
    {
        var allIds = new HashSet<string>(semantic.Keys);
        allIds.UnionWith(keyword.Keys);

        var fused = new List<HybridSearchResult>(allIds.Count);
        foreach (var id in allIds)
        {
            semantic.TryGetValue(id, out var sem);
            keyword.TryGetValue(id, out var kw);

            var entity = sem.Entity ?? kw.Entity;
            float semanticScore = sem.Score;
            float keywordScore = kw.Score;
            float fusedScore = semanticScore * options.SemanticWeight + keywordScore * options.KeywordWeight;

            float decayFactor = 1f;
            if (options.EnableDecay && nowMs > 0 && options.DecayHalfLifeDays > 0)
                decayFactor = CalculateDecayFactor(entity.LastAccessedAtMs, entity.CreatedAtMs, nowMs, options.DecayHalfLifeDays);

            fused.Add(new HybridSearchResult(entity, semanticScore, keywordScore, fusedScore * decayFactor, decayFactor));
        }

        fused.Sort((a, b) => b.FusedScore.CompareTo(a.FusedScore));

        return fused.Count <= options.TopK ? fused : fused.GetRange(0, options.TopK);
    }

    internal static float CalculateDecayFactor(long? lastAccessedAtMs, long createdAtMs, long nowMs, float halfLifeDays)
    {
        long referenceMs = lastAccessedAtMs ?? createdAtMs;
        long ageMs = nowMs - referenceMs;
        if (ageMs <= 0) return 1f;

        double ageInDays = ageMs / (1000.0 * 60 * 60 * 24);
        return (float)Math.Pow(2.0, -ageInDays / halfLifeDays);
    }

    internal static List<string> Tokenize(string text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in text.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries))
        {
            var lower = raw.Trim().ToLowerInvariant();
            if (lower.Length > 1)
                tokens.Add(lower);
        }
        return [.. tokens];
    }

    private static readonly char[] SplitChars =
        [' ', '\t', '\n', '\r', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}',
         '"', '\'', '/', '\\', '|', '+', '=', '<', '>', '~', '`', '@', '#', '$', '%', '^', '&', '*',
         '\u00B7',
         '\u3001', '\u3002', '\uFF01', '\uFF08', '\uFF09', '\uFF0C', '\uFF1A', '\uFF1B', '\uFF1F',
         '\u3010', '\u3011', '\u201C', '\u201D', '\u2018', '\u2019'];

    // ══════════════════════════════════════════════════════════════════════
    //  Internal helpers
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Search this RAG + all parents, merge and deduplicate.
    /// </summary>
    private async Task<List<HybridSearchResult>> SearchMergedAsync(
        string query, HybridSearchOptions options, CancellationToken ct)
    {
        // Kick off local + parent searches in parallel
        var localTask = SearchLocalAsync(query, options, ct);

        var parentTasks = Parents.Select(p => p.SearchLocalAsync(query, options, ct)).ToList();

        var allTasks = new List<Task<IReadOnlyList<HybridSearchResult>>>(parentTasks.Count + 1) { localTask };
        allTasks.AddRange(parentTasks);

        await Task.WhenAll(allTasks).ConfigureAwait(false);

        var allResults = new List<HybridSearchResult>();
        foreach (var task in allTasks)
            allResults.AddRange(task.Result);

        if (allResults.Count == 0) return [];

        // Deduplicate by chunk ID, keeping highest fused score
        return allResults
            .GroupBy(r => r.Record.Id)
            .Select(g => g.MaxBy(r => r.FusedScore)!)
            .OrderByDescending(r => r.FusedScore)
            .Take(options.TopK)
            .ToList();
    }

    /// <summary>Create a RagDbContext for the local database, with lazy schema init.</summary>
    private RagDbContext CreateDb()
    {
        string? dir = Path.GetDirectoryName(DbPath);
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var options = new DbContextOptionsBuilder<RagDbContext>()
            .UseSqlite($"Data Source={DbPath}")
            .Options;

        var context = new RagDbContext(options);

        if (_initialized.TryAdd(DbPath, true))
        {
            context.Database.EnsureCreated();
            EvolveSchema(context);
        }

        return context;
    }

    private static void EvolveSchema(RagDbContext context)
    {
        TryAddColumn(context, "ALTER TABLE vector_chunks ADD COLUMN last_accessed_at_ms INTEGER NULL");
        TryAddColumn(context, "ALTER TABLE vector_chunks ADD COLUMN hit_count INTEGER NOT NULL DEFAULT 0");
    }

    private static void TryAddColumn(RagDbContext context, string sql)
    {
        try { context.Database.ExecuteSqlRaw(sql); }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
    }

    private static List<TextChunk> ChunkText(string source)
    {
        var trimmed = source.TrimStart();
        return trimmed.StartsWith('#')
            ? TextChunker.ChunkMarkdown(source)
            : TextChunker.ChunkByTokens(source);
    }

    private static List<VectorChunkEntity> BuildEntities(
        List<TextChunk> chunks, IReadOnlyList<ReadOnlyMemory<float>> vectors, string sourceId, long createdAtMs)
    {
        return chunks.Select((chunk, i) => new VectorChunkEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceId = sourceId,
            Content = chunk.Content,
            VectorBlob = VectorHelper.ToBytes(vectors[i].Span),
            MetadataJson = JsonSerializer.Serialize(
                new { chunkIndex = chunk.Index, tokenCount = chunk.TokenCount }),
            CreatedAtMs = createdAtMs,
        }).ToList();
    }

    private static string? TryParseFileName(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.TryGetProperty("filename", out var prop))
                return prop.GetString();
        }
        catch (JsonException) { }
        return null;
    }

    private void FirePruneIfNeeded()
    {
        _ = Task.Run(async () =>
        {
            try { await PruneIfNeededAsync(); }
            catch { /* Logged inside PruneIfNeededAsync */ }
        });
    }

    private void RecordStatFireAndForget(long elapsedMs, int recallCount)
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int hitDelta = recallCount > 0 ? 1 : 0;

        _ = Task.Run(async () =>
        {
            try
            {
                using var db = CreateDb();
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO search_stats
                        (id, total_queries, hit_queries,
                         total_elapsed_ms, total_recall_count, last_updated_at_ms)
                    VALUES (1, 1, {hitDelta}, {elapsedMs}, {recallCount}, {nowMs})
                    ON CONFLICT(id) DO UPDATE SET
                        total_queries      = total_queries      + 1,
                        hit_queries        = hit_queries        + excluded.hit_queries,
                        total_elapsed_ms   = total_elapsed_ms   + excluded.total_elapsed_ms,
                        total_recall_count = total_recall_count + excluded.total_recall_count,
                        last_updated_at_ms = excluded.last_updated_at_ms;
                """).ConfigureAwait(false);
            }
            catch
            {
                // Stats failure does not affect main path
            }
        });
    }

    private async Task UpdateLastAccessedAsync(List<string> ids, long nowMs)
    {
        try
        {
            using var db = CreateDb();
            var chunks = await db.VectorChunks
                .Where(e => ids.Contains(e.Id))
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var chunk in chunks)
                chunk.LastAccessedAtMs = nowMs;

            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        catch
        {
            // Last-accessed update failure is non-critical
        }
    }
}
