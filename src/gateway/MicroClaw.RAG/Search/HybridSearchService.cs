using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MicroClaw.RAG;

/// <summary>
/// 混合检索服务 — 语义检索 + 关键词检索，加权融合排序，可选时间衰减。
/// </summary>
public sealed class HybridSearchService
{
    private readonly IEmbeddingService _embedding;
    private readonly RagDbContextFactory _dbFactory;
    private readonly ILogger<HybridSearchService> _logger;

    public HybridSearchService(IEmbeddingService embedding, RagDbContextFactory dbFactory, ILogger<HybridSearchService> logger)
    {
        _embedding = embedding ?? throw new ArgumentNullException(nameof(embedding));
        _dbFactory = dbFactory ?? throw new ArgumentNullException(nameof(dbFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>执行混合检索。</summary>
    public async Task<IReadOnlyList<HybridSearchResult>> SearchAsync(
        string query,
        RagScope scope,
        string? sessionId = null,
        HybridSearchOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        options ??= new HybridSearchOptions();

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // 两路并行检索
        var semanticTask = SemanticSearchAsync(query, scope, sessionId, options, ct);
        var keywordTask = KeywordSearchAsync(query, scope, sessionId, options, ct);
        await Task.WhenAll(semanticTask, keywordTask).ConfigureAwait(false);

        var results = Fuse(semanticTask.Result, keywordTask.Result, options, nowMs);

        // 异步更新命中分块的最近访问时间（fire-and-forget，不阻塞检索响应）
        if (results.Count > 0)
        {
            var ids = results.Select(r => r.Record.Id).ToList();
            _ = UpdateLastAccessedAsync(scope, sessionId, ids, nowMs);
        }

        return results;
    }

    /// <summary>语义检索：query → 向量化 → 余弦相似度 Top-K。embedding 失败时降级返回空结果。</summary>
    private async Task<Dictionary<string, (VectorChunkEntity Entity, float Score)>> SemanticSearchAsync(
        string query,
        RagScope scope,
        string? sessionId,
        HybridSearchOptions options,
        CancellationToken ct)
    {
        ReadOnlyMemory<float> queryVec;
        try
        {
            queryVec = await _embedding.GenerateAsync(query, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Embedding 向量化失败，语义检索将被跳过，降级为纯关键词检索");
            return new Dictionary<string, (VectorChunkEntity, float)>();
        }
        var candidateCount = options.TopK * options.SemanticCandidateMultiplier;

        using var db = _dbFactory.Create(scope, sessionId);
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

    /// <summary>关键词检索：简单分词 → 内容匹配 → 命中比例打分。</summary>
    private async Task<Dictionary<string, (VectorChunkEntity Entity, float Score)>> KeywordSearchAsync(
        string query,
        RagScope scope,
        string? sessionId,
        HybridSearchOptions options,
        CancellationToken ct)
    {
        var keywords = Tokenize(query);
        if (keywords.Count == 0)
            return new Dictionary<string, (VectorChunkEntity, float)>();

        using var db = _dbFactory.Create(scope, sessionId);
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

    /// <summary>加权融合两路结果，去重后按融合分数降序返回 TopK。支持可选时间衰减。</summary>
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

    /// <summary>
    /// 计算时间衰减因子：指数半衰期衰减 2^(-age/halfLife)。
    /// 使用 LastAccessedAtMs（若有）或 CreatedAtMs 作为参考时间。
    /// </summary>
    /// <returns>衰减因子，范围 (0, 1]，从未衰减时为 1.0。</returns>
    internal static float CalculateDecayFactor(long? lastAccessedAtMs, long createdAtMs, long nowMs, float halfLifeDays)
    {
        long referenceMs = lastAccessedAtMs ?? createdAtMs;
        long ageMs = nowMs - referenceMs;
        if (ageMs <= 0) return 1f;

        double ageInDays = ageMs / (1000.0 * 60 * 60 * 24);
        return (float)Math.Pow(2.0, -ageInDays / halfLifeDays);
    }

    /// <summary>
    /// 异步更新命中分块的最近访问时间和调用次数（fire-and-forget，不阻塞检索响应）。
    /// </summary>
    private async Task UpdateLastAccessedAsync(RagScope scope, string? sessionId, List<string> ids, long nowMs)
    {
        try
        {
            using var db = _dbFactory.Create(scope, sessionId);
            var chunks = await db.VectorChunks
                .Where(e => ids.Contains(e.Id))
                .ToListAsync()
                .ConfigureAwait(false);

            foreach (var chunk in chunks)
            {
                chunk.LastAccessedAtMs = nowMs;
                chunk.HitCount += 1;
            }

            await db.SaveChangesAsync().ConfigureAwait(false);
        }
        catch
        {
            // 访问时间/调用次数更新失败不影响主链路，静默忽略
        }
    }

    /// <summary>简单分词：按空白/标点分割，转小写，去重，过滤单字符。</summary>
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
}
