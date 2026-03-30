using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.VectorData;

namespace MicroClaw.RAG;

/// <summary>
/// SQLite 向量集合，基于 EF Core 实现 CRUD + C# 端余弦相似度搜索。
/// </summary>
public sealed class SqliteVectorCollection : VectorStoreCollection<string, VectorChunkEntity>
{
    private readonly RagDbContextFactory _factory;
    private readonly RagScope _scope;
    private readonly string? _sessionId;
    private readonly string _name;

    public override string Name => _name;

    public SqliteVectorCollection(
        string name,
        RagDbContextFactory factory,
        RagScope scope,
        string? sessionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(factory);
        _name = name;
        _factory = factory;
        _scope = scope;
        _sessionId = sessionId;
    }

    // ── 服务发现 ──

    public override object? GetService(Type serviceType, object? serviceKey = null) => null;

    // ── 集合管理 ──

    public override Task<bool> CollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        var path = _factory.ResolveDatabasePath(_scope, _sessionId);
        return Task.FromResult(File.Exists(path));
    }

    public override Task EnsureCollectionExistsAsync(CancellationToken cancellationToken = default)
    {
        using var db = _factory.Create(_scope, _sessionId);
        return Task.CompletedTask;
    }

    public override Task EnsureCollectionDeletedAsync(CancellationToken cancellationToken = default)
    {
        using var db = _factory.Create(_scope, _sessionId);
        db.Database.ExecuteSqlRaw("DROP TABLE IF EXISTS vector_chunks");
        return Task.CompletedTask;
    }

    // ── 单条 CRUD ──

    public override async Task<string> UpsertAsync(VectorChunkEntity record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var db = _factory.Create(_scope, _sessionId);

        var existing = await db.VectorChunks.FindAsync([record.Id], cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            db.VectorChunks.Add(record);
        }
        else
        {
            db.Entry(existing).CurrentValues.SetValues(record);
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return record.Id;
    }

    public override async Task<VectorChunkEntity?> GetAsync(string key, RecordRetrievalOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var db = _factory.Create(_scope, _sessionId);
        return await db.VectorChunks.AsNoTracking().FirstOrDefaultAsync(e => e.Id == key, cancellationToken).ConfigureAwait(false);
    }

    public override async Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        using var db = _factory.Create(_scope, _sessionId);
        await db.VectorChunks.Where(e => e.Id == key).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    // ── 批量操作 ──

    public override async Task UpsertAsync(
        IEnumerable<VectorChunkEntity> records,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(records);
        foreach (var record in records)
        {
            await UpsertAsync(record, cancellationToken).ConfigureAwait(false);
        }
    }

    public override async IAsyncEnumerable<VectorChunkEntity> GetAsync(
        IEnumerable<string> keys,
        RecordRetrievalOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        using var db = _factory.Create(_scope, _sessionId);

        var keyList = keys.ToList();
        var results = await db.VectorChunks
            .AsNoTracking()
            .Where(e => keyList.Contains(e.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var r in results)
            yield return r;
    }

    public override async IAsyncEnumerable<VectorChunkEntity> GetAsync(
        Expression<Func<VectorChunkEntity, bool>> filter,
        int top = 100,
        FilteredRecordRetrievalOptions<VectorChunkEntity>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        using var db = _factory.Create(_scope, _sessionId);

        var results = await db.VectorChunks
            .AsNoTracking()
            .Where(filter)
            .Take(top)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var r in results)
            yield return r;
    }

    public override async Task DeleteAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var keyList = keys.ToList();
        if (keyList.Count == 0) return;

        using var db = _factory.Create(_scope, _sessionId);
        await db.VectorChunks.Where(e => keyList.Contains(e.Id)).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    // ── 向量搜索 ──

    public override async IAsyncEnumerable<VectorSearchResult<VectorChunkEntity>> SearchAsync<TVector>(
        TVector vector,
        int top,
        VectorSearchOptions<VectorChunkEntity>? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ReadOnlyMemory<float> queryVec = vector switch
        {
            ReadOnlyMemory<float> rom => rom,
            float[] arr => arr.AsMemory(),
            _ => throw new NotSupportedException(
                $"不支持的向量类型: {typeof(TVector).Name}，请传入 ReadOnlyMemory<float> 或 float[]")
        };

        using var db = _factory.Create(_scope, _sessionId);

        var all = await db.VectorChunks.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);

        var scored = all
            .Where(e => e.VectorBlob.Length > 0)
            .Select(e => new
            {
                Entity = e,
                Score = VectorHelper.CosineSimilarity(queryVec.Span, VectorHelper.ToFloats(e.VectorBlob))
            })
            .OrderByDescending(x => x.Score)
            .Take(top)
            .Select(x => new VectorSearchResult<VectorChunkEntity>(x.Entity, x.Score));

        foreach (var result in scored)
            yield return result;
    }
}
