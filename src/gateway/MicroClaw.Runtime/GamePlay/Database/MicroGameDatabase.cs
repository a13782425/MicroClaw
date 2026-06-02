using MicroClaw.Database;
using SQLite;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Security.Principal;
using System.Text;

namespace MicroClaw.Runtime.GamePlay;
/// <summary>
/// 单方江湖的存档库（一江湖一个 .db 文件，合 W36 / D4）。
/// 与全局运营库 <see cref="GlobalDatabase"/> 隔离；每个实例持有独立连接。
/// </summary>
public sealed class MicroGameDatabase : IAsyncDisposable
{
    private readonly static ConcurrentDictionary<string, MicroGameDatabase> _caches = new ConcurrentDictionary<string, MicroGameDatabase>();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SQLiteAsyncConnection _db;

    public string WorldId { get; }

    private MicroGameDatabase(string worldId, SQLiteAsyncConnection db)
    {
        WorldId = worldId;
        _db = db;
    }

    /// <summary>
    /// 打开（或新建）某江湖的存档库并建表。
    /// </summary>
    public static async Task<MicroGameDatabase> OpenAsync(string worldId, string dbPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(worldId);
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);

        if (_caches.TryGetValue(worldId, out MicroGameDatabase? database))
            return database;


        string? dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var db = new SQLiteAsyncConnection(dbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.SharedCache);

        await db.ExecuteAsync("PRAGMA foreign_keys = ON;");
        await db.ExecuteAsync("PRAGMA journal_mode = WAL;");
        //await db.CreateTableAsync<WorldEntity>();
        //await db.CreateTableAsync<FactionEntity>();
        //await db.CreateTableAsync<MemberEntity>();
        //await db.CreateTableAsync<BountyEntity>();
        //await db.CreateTableAsync<BidEntity>();
        //await db.CreateTableAsync<LedgerEntryEntity>();
        //await db.CreateTableAsync<ReputationEventEntity>();
        database = new MicroGameDatabase(worldId, db);
        _caches.TryAdd(worldId, database);
        return database;

    }
    public Task<List<T>> GetAllAsync<T>() where T : class, IDatabaseEntity, new() => _db.Table<T>().ToListAsync();

    public Task<List<T>> QueryAsync<T>(Expression<Func<T, bool>> predicate) where T : class, IDatabaseEntity, new() => _db.Table<T>().Where(predicate).ToListAsync();

    public async Task InsertAsync<T>(T entity, CancellationToken ct = default) where T : class, IDatabaseEntity, new()
    {
        await _gate.WaitAsync(ct);
        try
        {
            await _db.InsertAsync(entity);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync<T>(T entity, CancellationToken ct = default) where T : class, IDatabaseEntity, new()
    {
        await _gate.WaitAsync(ct);
        try
        {
            await _db.UpdateAsync(entity);
        }
        finally
        {
            _gate.Release();
        }
    }
    /// <summary>
    /// 裸 SQL（联表 / 聚合 / 报表）。
    /// </summary>
    public Task<List<T>> RawQueryAsync<T>(string sql, params object[] args) where T : new() => _db.QueryAsync<T>(sql, args);
    public async ValueTask DisposeAsync()
    {
        await _db.CloseAsync();
        _gate.Dispose();
    }
}
