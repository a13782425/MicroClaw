using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace MicroClaw.Database;

/// <summary>
/// Static facade for the global database. Call <see cref="Initialize"/> once at startup before any other method.
/// </summary>
public static class GlobalDatabase
{
    private static IDbContextFactory<GlobalDbContext>? _factory;

    /// <summary>
    /// 初始化数据库
    /// </summary>
    public static void Initialize(string dbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        var options = new DbContextOptionsBuilder<GlobalDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        _factory = new PooledDbContextFactory<GlobalDbContext>(options);
        using var db = CreateContext();
        db.Database.Migrate();
    }

    internal static GlobalDbContext CreateContext() => _factory?.CreateDbContext() ?? throw new InvalidOperationException("Not initialized.");

    // ── Generic CRUD ──────────────────────────────────────────────────────

    public static async Task<List<T>> GetAllAsync<T>(CancellationToken ct = default) where T : class, IDatabaseEntity
    {
        await using var db = CreateContext();
        return await db.Set<T>().AsNoTracking().ToListAsync(ct);
    }

    public static async Task<List<T>> QueryAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default) where T : class, IDatabaseEntity
    {
        await using var db = CreateContext();
        return await db.Set<T>().AsNoTracking().Where(predicate).ToListAsync(ct);
    }

    public static async Task AddAsync<T>(T entity, CancellationToken ct = default) where T : class, IDatabaseEntity
    {
        ArgumentNullException.ThrowIfNull(entity);
        await using var db = CreateContext();
        db.Set<T>().Add(entity);
        await db.SaveChangesAsync(ct);
    }

    public static async Task UpdateAsync<T>(T entity, CancellationToken ct = default) where T : class, IDatabaseEntity
    {
        ArgumentNullException.ThrowIfNull(entity);
        await using var db = CreateContext();
        db.Set<T>().Update(entity);
        await db.SaveChangesAsync(ct);
    }

    public static async Task DeleteAsync<T>(Expression<Func<T, bool>> predicate, CancellationToken ct = default) where T : class, IDatabaseEntity
    {
        await using var db = CreateContext();
        await db.Set<T>().Where(predicate).ExecuteDeleteAsync(ct);
    }
}
