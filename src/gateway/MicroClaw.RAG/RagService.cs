using System.Diagnostics;
using System.Text;
using System.Text.Json;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.RAG;

/// <summary>
/// <see cref="IRagService"/> 完整实现。
/// <list type="bullet">
///   <item><see cref="IngestAsync"/>：文本 → 分块 → 批量嵌入 → 存入目标知识库 DB。</item>
///   <item><see cref="QueryAsync"/>：混合检索；Session 作用域合并 Global + Session 双库结果。</item>
///   <item>每次 <see cref="QueryAsync"/> 异步记录检索统计（fire-and-forget，不影响主链路延迟）。</item>
/// </list>
/// </summary>
public sealed class RagService : IRagService
{
    private readonly IEmbeddingService _embedding;
    private readonly RagDbContextFactory _dbFactory;
    private readonly HybridSearchService _hybridSearch;
    private readonly IDbContextFactory<GatewayDbContext>? _statsFactory;
    private readonly IRagPruner? _pruner;

    public RagService(
        IEmbeddingService embedding,
        RagDbContextFactory dbFactory,
        HybridSearchService hybridSearch,
        IDbContextFactory<GatewayDbContext>? statsFactory = null,
        IRagPruner? pruner = null)
    {
        _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _hybridSearch = hybridSearch ?? throw new ArgumentNullException(nameof(hybridSearch));
        _statsFactory = statsFactory;
        _pruner = pruner;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 自动选择分块策略：内容以 <c>#</c> 开头视为 Markdown，使用标题感知分块；
    /// 否则使用固定长度滑动窗口分块。
    /// 每次调用生成独立的 <c>sourceId</c>（GUID）。
    /// </remarks>
    public async Task IngestAsync(string source, RagScope scope, string? sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source)) return;

        // 选择分块策略：Markdown 标题感知 vs 滑动窗口
        var trimmed = source.TrimStart();
        var chunks = trimmed.StartsWith('#')
            ? TextChunker.ChunkMarkdown(source)
            : TextChunker.ChunkByTokens(source);

        if (chunks.Count == 0) return;

        // 批量生成嵌入向量
        var vectors = await _embedding
            .GenerateBatchAsync(chunks.Select(c => c.Content), ct)
            .ConfigureAwait(false);

        var sourceId = Guid.NewGuid().ToString("N");
        var createdAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var entities = chunks.Select((chunk, i) => new VectorChunkEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceId = sourceId,
            Content = chunk.Content,
            VectorBlob = VectorHelper.ToBytes(vectors[i].Span),
            MetadataJson = JsonSerializer.Serialize(
                new { chunkIndex = chunk.Index, tokenCount = chunk.TokenCount }),
            CreatedAtMs = createdAtMs,
        }).ToList();

        using var db = _dbFactory.Create(scope, sessionId);
        db.VectorChunks.AddRange(entities);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        FirePruneIfNeeded(scope, sessionId);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><see cref="RagScope.Global"/>：仅检索全局知识库。</item>
    ///   <item><see cref="RagScope.Session"/>（含 sessionId）：并行检索全局 + 会话知识库，去重合并。</item>
    /// </list>
    /// 按融合分数降序取前 Top-10 条，以 <c>"\n---\n"</c> 拼接返回；无结果时返回空字符串。
    /// </remarks>
    public async Task<string> QueryAsync(string query, RagScope scope, string? sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var options = new HybridSearchOptions();
        var sw = Stopwatch.StartNew();

        // 始终检索全局库
        var globalTask = _hybridSearch.SearchAsync(query, RagScope.Global, null, options, ct);

        // Session 作用域：额外并行检索会话库
        Task<IReadOnlyList<HybridSearchResult>>? sessionTask = null;
        if (scope == RagScope.Session && sessionId is not null)
            sessionTask = _hybridSearch.SearchAsync(query, RagScope.Session, sessionId, options, ct);

        var allResults = new List<HybridSearchResult>();

        if (sessionTask is not null)
        {
            await Task.WhenAll(globalTask, sessionTask).ConfigureAwait(false);
            allResults.AddRange(globalTask.Result);
            allResults.AddRange(sessionTask.Result);
        }
        else
        {
            allResults.AddRange(await globalTask.ConfigureAwait(false));
        }

        if (allResults.Count == 0)
        {
            sw.Stop();
            RecordStatFireAndForget(scope, sw.ElapsedMilliseconds, 0);
            return string.Empty;
        }

        // 去重（同 Id 取最高融合分）→ 降序排列 → 取前 TopK
        var merged = allResults
            .GroupBy(r => r.Record.Id)
            .Select(g => g.MaxBy(r => r.FusedScore)!)
            .OrderByDescending(r => r.FusedScore)
            .Take(options.TopK)
            .ToList();

        sw.Stop();
        RecordStatFireAndForget(scope, sw.ElapsedMilliseconds, merged.Count);

        return string.Join("\n---\n", merged.Select(r => r.Record.Content));
    }

    private void RecordStatFireAndForget(RagScope scope, long elapsedMs, int recallCount)
    {
        if (_statsFactory is null) return;

        _ = Task.Run(async () =>
        {
            try
            {
                using var db = _statsFactory.CreateDbContext();
                db.RagSearchStats.Add(new RagSearchStatEntity
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Scope = scope.ToString(),
                    ElapsedMs = elapsedMs,
                    RecallCount = recallCount,
                    RecordedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                });
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            catch
            {
                // 统计失败不影响主链路
            }
        });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 与 <see cref="IngestAsync(string, RagScope, string?, CancellationToken)"/> 相同，
    /// 但使用调用方提供的固定 <paramref name="sourceId"/>，支持增量索引去重场景。
    /// </remarks>
    public async Task IngestAsync(string source, string sourceId, RagScope scope, string? sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(source)) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        // 幂等检测：若 DB 中已存在该 sourceId 的分块则跳过
        using var existCheck = _dbFactory.Create(scope, sessionId);
        if (await existCheck.VectorChunks.AsNoTracking().AnyAsync(e => e.SourceId == sourceId, ct).ConfigureAwait(false))
            return;

        var trimmed = source.TrimStart();
        var chunks = trimmed.StartsWith('#')
            ? TextChunker.ChunkMarkdown(source)
            : TextChunker.ChunkByTokens(source);

        if (chunks.Count == 0) return;

        var vectors = await _embedding
            .GenerateBatchAsync(chunks.Select(c => c.Content), ct)
            .ConfigureAwait(false);

        var createdAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var entities = chunks.Select((chunk, i) => new VectorChunkEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceId = sourceId,
            Content = chunk.Content,
            VectorBlob = VectorHelper.ToBytes(vectors[i].Span),
            MetadataJson = JsonSerializer.Serialize(
                new { chunkIndex = chunk.Index, tokenCount = chunk.TokenCount }),
            CreatedAtMs = createdAtMs,
        }).ToList();

        using var db = _dbFactory.Create(scope, sessionId);
        db.VectorChunks.AddRange(entities);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        FirePruneIfNeeded(scope, sessionId);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlySet<string>> GetIndexedSourceIdsAsync(RagScope scope, string? sessionId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create(scope, sessionId);
        var ids = await db.VectorChunks
            .AsNoTracking()
            .Select(e => e.SourceId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return ids.ToHashSet(StringComparer.Ordinal);
    }

    /// <inheritdoc/>
    public async Task<string> IngestDocumentAsync(string source, string fileName, RagScope scope, string? sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var sourceId = $"doc:{fileName}";

        // 若同名文档已存在则先删除旧分块（增量重索引）
        await DeleteBySourceIdAsync(sourceId, scope, sessionId, ct).ConfigureAwait(false);

        var trimmed = source.TrimStart();
        var chunks = trimmed.StartsWith('#')
            ? TextChunker.ChunkMarkdown(source)
            : TextChunker.ChunkByTokens(source);

        if (chunks.Count == 0) return sourceId;

        var vectors = await _embedding
            .GenerateBatchAsync(chunks.Select(c => c.Content), ct)
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

        using var db = _dbFactory.Create(scope, sessionId);
        db.VectorChunks.AddRange(entities);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        FirePruneIfNeeded(scope, sessionId);

        return sourceId;
    }

    /// <inheritdoc/>
    public async Task DeleteBySourceIdAsync(string sourceId, RagScope scope, string? sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);

        using var db = _dbFactory.Create(scope, sessionId);
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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RagDocumentInfo>> ListDocumentsAsync(RagScope scope, string? sessionId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create(scope, sessionId);

        // 仅返回 doc: 前缀的 SourceId（文档上传），排除 msg: 等其他来源
        var chunks = await db.VectorChunks
            .AsNoTracking()
            .Where(e => e.SourceId.StartsWith("doc:"))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var docs = chunks
            .GroupBy(e => e.SourceId)
            .Select(g =>
            {
                var first = g.OrderBy(e => e.CreatedAtMs).First();
                // 从 MetadataJson 读取 filename 字段，降级时从 SourceId 解析
                string fileName = TryParseFileName(first.MetadataJson) ?? g.Key["doc:".Length..];
                return new RagDocumentInfo(
                    SourceId: g.Key,
                    FileName: fileName,
                    ChunkCount: g.Count(),
                    IndexedAtMs: first.CreatedAtMs);
            })
            .OrderBy(d => d.FileName)
            .ToList();

        return docs;
    }

    /// <inheritdoc/>
    public async Task<RagQueryStats> GetQueryStatsAsync(RagScope? scope, CancellationToken ct = default)
    {
        if (_statsFactory is null)
            return new RagQueryStats(scope?.ToString() ?? "All", 0, 0, 0, 0, 0, 0);

        using var db = _statsFactory.CreateDbContext();

        IQueryable<RagSearchStatEntity> query = db.RagSearchStats.AsNoTracking();
        if (scope.HasValue)
            query = query.Where(e => e.Scope == scope.Value.ToString());

        var stats = await query.ToListAsync(ct).ConfigureAwait(false);

        if (stats.Count == 0)
            return new RagQueryStats(scope?.ToString() ?? "All", 0, 0, 0, 0, 0, 0);

        long total = stats.Count;
        long hits = stats.Count(e => e.RecallCount > 0);
        double hitRate = (double)hits / total;
        double avgElapsed = stats.Average(e => e.ElapsedMs);
        double avgRecall = stats.Average(e => e.RecallCount);

        long cutoff24h = DateTimeOffset.UtcNow.AddHours(-24).ToUnixTimeMilliseconds();
        long last24h = stats.Count(e => e.RecordedAtMs >= cutoff24h);

        return new RagQueryStats(
            Scope: scope?.ToString() ?? "All",
            TotalQueries: total,
            HitQueries: hits,
            HitRate: hitRate,
            AvgElapsedMs: Math.Round(avgElapsed, 1),
            AvgRecallCount: Math.Round(avgRecall, 2),
            Last24hQueries: last24h);
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

    /// <summary>
    /// Fire-and-forget prune check after ingest. Does not block the ingest response.
    /// </summary>
    private void FirePruneIfNeeded(RagScope scope, string? sessionId)
    {
        if (_pruner is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await _pruner.PruneIfNeededAsync(scope, sessionId);
            }
            catch (Exception)
            {
                // Logged inside RagPruner; swallow here to avoid unobserved task exception.
            }
        });
    }

    /// <inheritdoc/>
    public async Task<RagQueryResult> QueryWithMetadataAsync(
        string query, RagScope scope, string? sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var options = new HybridSearchOptions();
        var sw = Stopwatch.StartNew();

        var globalTask = _hybridSearch.SearchAsync(query, RagScope.Global, null, options, ct);

        Task<IReadOnlyList<HybridSearchResult>>? sessionTask = null;
        if (scope == RagScope.Session && sessionId is not null)
            sessionTask = _hybridSearch.SearchAsync(query, RagScope.Session, sessionId, options, ct);

        var allResults = new List<HybridSearchResult>();

        if (sessionTask is not null)
        {
            await Task.WhenAll(globalTask, sessionTask).ConfigureAwait(false);
            // Tag global results
            foreach (var r in globalTask.Result)
                allResults.Add(r);
            foreach (var r in sessionTask.Result)
                allResults.Add(r);
        }
        else
        {
            allResults.AddRange(await globalTask.ConfigureAwait(false));
        }

        if (allResults.Count == 0)
        {
            sw.Stop();
            RecordStatFireAndForget(scope, sw.ElapsedMilliseconds, 0);
            return new RagQueryResult(string.Empty, []);
        }

        var merged = allResults
            .GroupBy(r => r.Record.Id)
            .Select(g => g.MaxBy(r => r.FusedScore)!)
            .OrderByDescending(r => r.FusedScore)
            .Take(options.TopK)
            .ToList();

        sw.Stop();
        RecordStatFireAndForget(scope, sw.ElapsedMilliseconds, merged.Count);

        // Build chunk refs with scope info
        var chunkRefs = merged.Select(r =>
        {
            // Determine scope: if the chunk was found in session search, tag as Session
            bool isSessionChunk = sessionTask is not null &&
                                  sessionTask.Result.Any(sr => sr.Record.Id == r.Record.Id);
            return new RagChunkRef(
                r.Record.Id,
                r.Record.Content,
                r.Record.HitCount,
                r.Record.SourceId,
                isSessionChunk ? RagScope.Session : RagScope.Global,
                isSessionChunk ? sessionId : null);
        }).ToList();

        // Format content with hit count annotation
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

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RagChunkInfo>> ListChunksAsync(
        RagScope scope, string? sessionId, CancellationToken ct = default)
    {
        using var db = _dbFactory.Create(scope, sessionId);
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

    /// <inheritdoc/>
    public async Task DeleteChunkAsync(
        string chunkId, RagScope scope, string? sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);

        using var db = _dbFactory.Create(scope, sessionId);
        var chunk = await db.VectorChunks
            .FirstOrDefaultAsync(e => e.Id == chunkId, ct)
            .ConfigureAwait(false);

        if (chunk is not null)
        {
            db.VectorChunks.Remove(chunk);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task UpdateChunkHitCountAsync(
        string chunkId, int hitCount, RagScope scope, string? sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chunkId);
        if (hitCount < 0) throw new ArgumentOutOfRangeException(nameof(hitCount), "HitCount must be >= 0.");

        using var db = _dbFactory.Create(scope, sessionId);
        var chunk = await db.VectorChunks
            .FirstOrDefaultAsync(e => e.Id == chunkId, ct)
            .ConfigureAwait(false);

        if (chunk is not null)
        {
            chunk.HitCount = hitCount;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task IncrementHitCountAsync(
        IReadOnlyList<string> chunkIds, RagScope scope, string? sessionId, CancellationToken ct = default)
    {
        if (chunkIds.Count == 0) return;

        using var db = _dbFactory.Create(scope, sessionId);
        var chunks = await db.VectorChunks
            .Where(e => chunkIds.Contains(e.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        foreach (var chunk in chunks)
            chunk.HitCount += 1;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
