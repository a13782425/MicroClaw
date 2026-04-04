using System.Collections.Concurrent;
using System.Text.Json;
using MicroClaw.Configuration;
using MicroClaw.RAG;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet.Rag;

/// <summary>
/// Pet 私有 RAG 操作封装。
/// <para>
/// 路径 <c>{sessionsDir}/{sessionId}/pet/knowledge.db</c>，与主 Session RAG（<c>{sessionId}/rag.db</c>）隔离。
/// 复用 <see cref="RagDbContext"/> / <see cref="TextChunker"/> / <see cref="IEmbeddingService"/> 基础设施。
/// </para>
/// </summary>
public sealed class PetRagScope
{
    private readonly IEmbeddingService _embedding;
    private readonly string _sessionsDir;
    private readonly ILogger<PetRagScope> _logger;
    private readonly ConcurrentDictionary<string, bool> _initialized = new(StringComparer.OrdinalIgnoreCase);

    private const double DefaultMaxStorageMb = 20;
    private const double DefaultPruneTargetPercent = 0.8;

    public PetRagScope(IEmbeddingService embedding, MicroClawConfigEnv env, ILogger<PetRagScope> logger)
    {
        _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        ArgumentNullException.ThrowIfNull(env);
        _sessionsDir = env.SessionsDir;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>仅供测试使用：直接指定 sessions 根目录。</summary>
    internal PetRagScope(IEmbeddingService embedding, string sessionsDir, ILogger<PetRagScope> logger)
    {
        _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionsDir);
        _sessionsDir = sessionsDir;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>获取指定 Session 的 Pet RAG 数据库路径。</summary>
    public string GetDatabasePath(string sessionId) =>
        Path.Combine(_sessionsDir, sessionId, "pet", "knowledge.db");

    /// <summary>
    /// 向 Pet RAG 注入文本内容。
    /// 自动选择分块策略（Markdown 标题感知 vs 固定 token 滑窗），批量嵌入后存入 Pet 私有知识库。
    /// </summary>
    /// <param name="content">待注入的文本内容。</param>
    /// <param name="sessionId">Session ID。</param>
    /// <param name="sourceId">可选 sourceId（用于去重），不提供则自动生成。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task IngestAsync(string content, string sessionId, string? sourceId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(content)) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var trimmed = content.TrimStart();
        var chunks = trimmed.StartsWith('#')
            ? TextChunker.ChunkMarkdown(content)
            : TextChunker.ChunkByTokens(content);

        if (chunks.Count == 0) return;

        var vectors = await _embedding
            .GenerateBatchAsync(chunks.Select(c => c.Content), ct)
            .ConfigureAwait(false);

        var sid = sourceId ?? Guid.NewGuid().ToString("N");
        var createdAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        using var db = CreateDb(sessionId);

        // 幂等检测：若指定了 sourceId 且已存在则跳过
        if (sourceId is not null)
        {
            if (await db.VectorChunks.AsNoTracking().AnyAsync(e => e.SourceId == sourceId, ct).ConfigureAwait(false))
                return;
        }

        var entities = chunks.Select((chunk, i) => new VectorChunkEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            SourceId = sid,
            Content = chunk.Content,
            VectorBlob = VectorHelper.ToBytes(vectors[i].Span),
            MetadataJson = JsonSerializer.Serialize(
                new { chunkIndex = chunk.Index, tokenCount = chunk.TokenCount }),
            CreatedAtMs = createdAtMs,
        }).ToList();

        db.VectorChunks.AddRange(entities);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 从 Pet RAG 库中混合检索（语义 + 关键词）。
    /// </summary>
    /// <param name="query">查询文本。</param>
    /// <param name="sessionId">Session ID。</param>
    /// <param name="topK">返回前 K 条结果。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>拼接的检索结果文本，无结果时返回空字符串。</returns>
    public async Task<string> QueryAsync(string query, string sessionId, int topK = 5, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var dbPath = GetDatabasePath(sessionId);
        if (!File.Exists(dbPath)) return string.Empty;

        ReadOnlyMemory<float> queryVec;
        try
        {
            queryVec = await _embedding.GenerateAsync(query, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Pet RAG: Embedding 失败，跳过语义检索");
            return string.Empty;
        }

        using var db = CreateDb(sessionId);
        var all = await db.VectorChunks.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        if (all.Count == 0) return string.Empty;

        var keywords = Tokenize(query);

        var scored = all
            .Select(e =>
            {
                float semanticScore = e.VectorBlob.Length > 0
                    ? VectorHelper.CosineSimilarity(queryVec.Span, VectorHelper.ToFloats(e.VectorBlob))
                    : 0f;

                float keywordScore = 0f;
                if (keywords.Count > 0)
                {
                    var lower = e.Content.ToLowerInvariant();
                    int hits = keywords.Count(kw => lower.Contains(kw, StringComparison.Ordinal));
                    keywordScore = (float)hits / keywords.Count;
                }

                float fused = semanticScore * 0.7f + keywordScore * 0.3f;
                return (Entity: e, Score: fused);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .ToList();

        if (scored.Count == 0) return string.Empty;

        return string.Join("\n---\n", scored.Select(r => r.Entity.Content));
    }

    /// <summary>获取 Pet RAG 库中的分块总数。</summary>
    public async Task<int> GetChunkCountAsync(string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var dbPath = GetDatabasePath(sessionId);
        if (!File.Exists(dbPath)) return 0;

        using var db = CreateDb(sessionId);
        return await db.VectorChunks.CountAsync(ct).ConfigureAwait(false);
    }

    /// <summary>删除指定 SourceId 的所有分块。</summary>
    public async Task DeleteBySourceIdAsync(string sourceId, string sessionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var dbPath = GetDatabasePath(sessionId);
        if (!File.Exists(dbPath)) return;

        using var db = CreateDb(sessionId);
        var targets = await db.VectorChunks
            .Where(c => c.SourceId == sourceId)
            .ToListAsync(ct)
            .ConfigureAwait(false);
        if (targets.Count == 0) return;

        db.VectorChunks.RemoveRange(targets);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 返回所有存在 Pet knowledge.db 的 Session ID 列表。
    /// </summary>
    public IReadOnlyList<string> GetAllPetSessionIds()
    {
        if (!Directory.Exists(_sessionsDir))
            return [];

        var petDbName = Path.Combine("pet", "knowledge.db");
        return Directory.GetFiles(_sessionsDir, "knowledge.db", SearchOption.AllDirectories)
            .Where(p => p.Contains(petDbName, StringComparison.OrdinalIgnoreCase))
            .Select(p =>
            {
                // 路径: {sessionsDir}/{sessionId}/pet/knowledge.db
                var petDir = Path.GetDirectoryName(p)!;       // .../pet
                var sessionDir = Path.GetDirectoryName(petDir)!; // .../{sessionId}
                return Path.GetFileName(sessionDir);
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// 对指定 Session 的 Pet RAG 执行容量清理。
    /// 删除 HitCount 最低的分块（最早创建的优先），直到存储低于目标阈值。
    /// </summary>
    public async Task PruneIfNeededAsync(string sessionId, double maxStorageMb = DefaultMaxStorageMb, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var dbPath = GetDatabasePath(sessionId);
        if (!File.Exists(dbPath)) return;

        long fileSizeBytes = new FileInfo(dbPath).Length;
        double fileSizeMb = fileSizeBytes / (1024.0 * 1024.0);

        if (fileSizeMb <= maxStorageMb) return;

        double targetSizeMb = maxStorageMb * DefaultPruneTargetPercent;
        _logger.LogInformation(
            "PetRagScope: Session {SessionId} Pet RAG {CurrentMb:F2}MB > {MaxMb:F2}MB，开始清理到 {TargetMb:F2}MB",
            sessionId, fileSizeMb, maxStorageMb, targetSizeMb);

        int totalDeleted = 0;
        const int batchSize = 50;

        while (!ct.IsCancellationRequested)
        {
            fileSizeBytes = new FileInfo(dbPath).Length;
            fileSizeMb = fileSizeBytes / (1024.0 * 1024.0);
            if (fileSizeMb <= targetSizeMb) break;

            using var db = CreateDb(sessionId);
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
            double finalSizeMb = new FileInfo(dbPath).Length / (1024.0 * 1024.0);
            _logger.LogInformation(
                "PetRagScope: Session {SessionId} Pet RAG 清理完成，删除 {Count} chunks，{BeforeMb:F2}MB → {AfterMb:F2}MB",
                sessionId, totalDeleted, fileSizeMb, finalSizeMb);
        }
    }

    private RagDbContext CreateDb(string sessionId)
    {
        var dbPath = GetDatabasePath(sessionId);
        var dir = Path.GetDirectoryName(dbPath);
        if (dir is not null) Directory.CreateDirectory(dir);

        var options = new DbContextOptionsBuilder<RagDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var ctx = new RagDbContext(options);

        if (_initialized.TryAdd(dbPath, true))
        {
            ctx.Database.EnsureCreated();
            TryAddColumn(ctx, "ALTER TABLE vector_chunks ADD COLUMN last_accessed_at_ms INTEGER NULL");
            TryAddColumn(ctx, "ALTER TABLE vector_chunks ADD COLUMN hit_count INTEGER NOT NULL DEFAULT 0");
        }

        return ctx;
    }

    /// <summary>
    /// 释放指定 Session 的 Pet RAG SQLite 连接池，在删除会话目录之前调用，
    /// 以避免 Windows 上 SQLite WAL 文件锁定导致目录删除失败。
    /// </summary>
    public void CloseDatabase(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var dbPath = GetDatabasePath(sessionId);
        _initialized.TryRemove(dbPath, out _);
        SqliteConnection.ClearAllPools();
    }

    private static void TryAddColumn(RagDbContext ctx, string sql)
    {
        try { ctx.Database.ExecuteSqlRaw(sql); }
        catch (SqliteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase)) { }
    }

    private static List<string> Tokenize(string text)
    {
        return text.ToLowerInvariant()
            .Split([' ', '\t', '\n', '\r',
                     '\uFF0C', '\u3002', '\uFF01', '\uFF1F', '\u3001', '\uFF1B', '\uFF1A',
                     '\u201C', '\u201D', '\u2018', '\u2019',
                     ',', '.', '!', '?', ';', ':', '"', '\'', '(', ')', '[', ']', '{', '}'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(w => w.Length >= 2)
            .Distinct()
            .ToList();
    }
}
