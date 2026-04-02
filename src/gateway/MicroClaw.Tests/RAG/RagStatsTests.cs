using FluentAssertions;
using Microsoft.Data.Sqlite;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.RAG;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MicroClaw.Tests.RAG;

public class RagStatsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly TestGatewayDbContextFactoryForRagStats _statsFactory;

    public RagStatsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mc_ragstats_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _statsFactory = new TestGatewayDbContextFactoryForRagStats(_tempDir);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── RagSearchStatEntity 持久化 ──

    [Fact]
    public async Task SaveAndQuery_SingleStat_ShouldPersist()
    {
        using var db = _statsFactory.CreateDbContext();
        var entity = new RagSearchStatEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            Scope = "Global",
            ElapsedMs = 250,
            RecallCount = 5,
            RecordedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        db.RagSearchStats.Add(entity);
        await db.SaveChangesAsync();

        using var db2 = _statsFactory.CreateDbContext();
        var saved = db2.RagSearchStats.Find(entity.Id);
        saved.Should().NotBeNull();
        saved!.Scope.Should().Be("Global");
        saved.ElapsedMs.Should().Be(250);
        saved.RecallCount.Should().Be(5);
    }

    // ── RagQueryStats 聚合 ──

    [Fact]
    public void RagQueryStats_ZeroData_ShouldHaveZeroFields()
    {
        var stats = new RagQueryStats("All", 0, 0, 0, 0, 0, 0);
        stats.TotalQueries.Should().Be(0);
        stats.HitRate.Should().Be(0);
        stats.AvgElapsedMs.Should().Be(0);
    }

    [Fact]
    public void RagQueryStats_Record_ShouldBeImmutable()
    {
        var stats = new RagQueryStats("Global", 10, 8, 0.8, 120.5, 3.2, 5);
        stats.TotalQueries.Should().Be(10);
        stats.HitQueries.Should().Be(8);
        stats.HitRate.Should().Be(0.8);
        stats.AvgElapsedMs.Should().Be(120.5);
        stats.AvgRecallCount.Should().Be(3.2);
        stats.Last24hQueries.Should().Be(5);
        stats.Scope.Should().Be("Global");
    }

    // ── RagService.GetQueryStatsAsync ──

    private RagService CreateRagService()
    {
        var ragFactory = new RagDbContextFactory(_tempDir);
        var embedding = Substitute.For<IEmbeddingService>();
        var fixedVec = new ReadOnlyMemory<float>([1f, 0f, 0f]);
        embedding.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(fixedVec));
        embedding.GenerateBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var texts = ci.Arg<IEnumerable<string>>().ToList();
                IReadOnlyList<ReadOnlyMemory<float>> vecs = texts
                    .Select(_ => new ReadOnlyMemory<float>([1f, 0f, 0f]))
                    .ToList();
                return Task.FromResult(vecs);
            });
        var hybridSearch = new HybridSearchService(embedding, ragFactory, NullLogger<HybridSearchService>.Instance);
        return new RagService(embedding, ragFactory, hybridSearch, _statsFactory);
    }

    [Fact]
    public async Task GetQueryStatsAsync_NoData_ShouldReturnZeroStats()
    {
        var sut = CreateRagService();
        var stats = await sut.GetQueryStatsAsync(null);
        stats.TotalQueries.Should().Be(0);
        stats.HitRate.Should().Be(0);
        stats.AvgElapsedMs.Should().Be(0);
        stats.Scope.Should().Be("All");
    }

    [Fact]
    public async Task GetQueryStatsAsync_NoStatsFactory_ShouldReturnZeroStats()
    {
        var ragFactory = new RagDbContextFactory(_tempDir);
        var embedding = Substitute.For<IEmbeddingService>();
        var hybridSearch = new HybridSearchService(embedding, ragFactory, NullLogger<HybridSearchService>.Instance);
        var sut = new RagService(embedding, ragFactory, hybridSearch); // 不注入 statsFactory

        var stats = await sut.GetQueryStatsAsync(null);
        stats.TotalQueries.Should().Be(0);
    }

    [Fact]
    public async Task GetQueryStatsAsync_AfterRecordingStats_ShouldAggregateCorrectly()
    {
        // 直接写入统计数据
        using (var db = _statsFactory.CreateDbContext())
        {
            db.RagSearchStats.AddRange(
                new RagSearchStatEntity { Id = "s1", Scope = "Global", ElapsedMs = 100, RecallCount = 5, RecordedAtMs = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds() },
                new RagSearchStatEntity { Id = "s2", Scope = "Global", ElapsedMs = 200, RecallCount = 0, RecordedAtMs = DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeMilliseconds() },
                new RagSearchStatEntity { Id = "s3", Scope = "Session", ElapsedMs = 150, RecallCount = 3, RecordedAtMs = DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeMilliseconds() }
            );
            await db.SaveChangesAsync();
        }

        var sut = CreateRagService();

        // 全部作用域统计
        var allStats = await sut.GetQueryStatsAsync(null);
        allStats.TotalQueries.Should().Be(3);
        allStats.HitQueries.Should().Be(2);
        allStats.HitRate.Should().BeApproximately(2.0 / 3.0, 0.01);
        allStats.Last24hQueries.Should().Be(3);
        allStats.Scope.Should().Be("All");

        // 仅 Global 统计
        var globalStats = await sut.GetQueryStatsAsync(RagScope.Global);
        globalStats.TotalQueries.Should().Be(2);
        globalStats.HitQueries.Should().Be(1);
        globalStats.HitRate.Should().BeApproximately(0.5, 0.01);
        globalStats.AvgElapsedMs.Should().BeApproximately(150.0, 0.1);
        globalStats.Scope.Should().Be("Global");

        // 仅 Session 统计
        var sessionStats = await sut.GetQueryStatsAsync(RagScope.Session);
        sessionStats.TotalQueries.Should().Be(1);
        sessionStats.HitQueries.Should().Be(1);
        sessionStats.HitRate.Should().Be(1.0);
        sessionStats.Scope.Should().Be("Session");
    }

    [Fact]
    public async Task GetQueryStatsAsync_Last24h_ShouldExcludeOldRecords()
    {
        using (var db = _statsFactory.CreateDbContext())
        {
            db.RagSearchStats.AddRange(
                new RagSearchStatEntity { Id = "old1", Scope = "Global", ElapsedMs = 100, RecallCount = 2, RecordedAtMs = DateTimeOffset.UtcNow.AddHours(-25).ToUnixTimeMilliseconds() },
                new RagSearchStatEntity { Id = "new1", Scope = "Global", ElapsedMs = 120, RecallCount = 3, RecordedAtMs = DateTimeOffset.UtcNow.AddMinutes(-30).ToUnixTimeMilliseconds() }
            );
            await db.SaveChangesAsync();
        }

        var sut = CreateRagService();
        var stats = await sut.GetQueryStatsAsync(RagScope.Global);
        stats.TotalQueries.Should().Be(2); // 总数包含所有记录
        stats.Last24hQueries.Should().Be(1); // 近 24h 仅 1 条
    }
}

/// <summary>RagStatsTests 专用 GatewayDbContext 工厂，使用 SQLite 文件数据库。</summary>
internal sealed class TestGatewayDbContextFactoryForRagStats : IDbContextFactory<GatewayDbContext>
{
    private readonly DbContextOptions<GatewayDbContext> _opts;

    public TestGatewayDbContextFactoryForRagStats(string tempDir)
    {
        _opts = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseSqlite($"Data Source={Path.Combine(tempDir, "test.db")}")
            .Options;
        using var ctx = new GatewayDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public GatewayDbContext CreateDbContext() => new(_opts);
}
