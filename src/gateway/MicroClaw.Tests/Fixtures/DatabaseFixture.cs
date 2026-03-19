using MicroClaw.Infrastructure.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MicroClaw.Tests.Fixtures;

/// <summary>
/// 提供 SQLite In-Memory 数据库的 IDbContextFactory，每个实例使用独立的数据库连接。
/// 实现 IDisposable 以在测试结束后关闭连接。
/// </summary>
public sealed class DatabaseFixture : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<GatewayDbContext> _options;

    public DatabaseFixture()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseSqlite(_connection)
            .Options;

        using GatewayDbContext db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public GatewayDbContext CreateDbContext() => new(_options);

    public IDbContextFactory<GatewayDbContext> CreateFactory() => new InMemoryDbContextFactory(_options);

    public void Dispose() => _connection.Dispose();

    private sealed class InMemoryDbContextFactory(DbContextOptions<GatewayDbContext> options)
        : IDbContextFactory<GatewayDbContext>
    {
        public GatewayDbContext CreateDbContext() => new(options);
    }
}
