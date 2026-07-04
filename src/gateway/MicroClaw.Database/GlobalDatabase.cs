using System.Linq.Expressions;
using SQLite;

namespace MicroClaw.Database;

/// <summary>
/// Static facade for the global database. Call <see cref="Initialize"/> once at startup before any other method.
/// </summary>
public static class GlobalDatabase
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static SQLiteAsyncConnection? _db;

    /// <summary>
    /// 初始化数据库
    /// </summary>
    public static async Task InitializeAsync(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        string? dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        _db = new SQLiteAsyncConnection(
            dbPath,
            SQLiteOpenFlags.ReadWrite |
            SQLiteOpenFlags.Create);
        var db = CreateConnection();
        await db.ExecuteAsync("PRAGMA foreign_keys = ON;");
        string mode = await db.ExecuteScalarAsync<string>("PRAGMA journal_mode = WAL;");
        // mode 正常会是 "wal"；可选做个校验
        await db.CreateTableAsync<TokenDailyEntity>();
        await db.CreateTableAsync<CallDailyEntity>();
    }

    internal static SQLiteAsyncConnection CreateConnection() =>
        _db ?? throw new InvalidOperationException("GlobalDatabase is not initialized.");

    // ── Generic CRUD ──────────────────────────────────────────────────────

    public static async Task<List<T>> GetAllAsync<T>(CancellationToken ct = default) where T : class, IDatabaseEntity, new()
    {
        ct.ThrowIfCancellationRequested();
        return await CreateConnection().Table<T>().ToListAsync();
    }

    public static async Task<List<T>> QueryAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default) where T : class, IDatabaseEntity, new()
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ct.ThrowIfCancellationRequested();
        return await CreateConnection().Table<T>().Where(predicate).ToListAsync();
    }

    public static async Task AddAsync<T>(T entity, CancellationToken ct = default) where T : class, IDatabaseEntity, new()
    {
        ArgumentNullException.ThrowIfNull(entity);
        ct.ThrowIfCancellationRequested();

        await Gate.WaitAsync(ct);
        try
        {
            await CreateConnection().InsertAsync(entity);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task UpdateAsync<T>(T entity, CancellationToken ct = default) where T : class, IDatabaseEntity, new()
    {
        ArgumentNullException.ThrowIfNull(entity);
        ct.ThrowIfCancellationRequested();

        await Gate.WaitAsync(ct);
        try
        {
            await CreateConnection().UpdateAsync(entity);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task DeleteAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default) where T : class, IDatabaseEntity, new()
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ct.ThrowIfCancellationRequested();

        await Gate.WaitAsync(ct);
        try
        {
            var db = CreateConnection();
            var rows = await db.Table<T>().Where(predicate).ToListAsync();
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                await db.DeleteAsync(row);
            }
        }
        finally
        {
            Gate.Release();
        }
    }
}
