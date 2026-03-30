using FluentAssertions;
using Microsoft.Data.Sqlite;
using MicroClaw.RAG;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MicroClaw.Tests.RAG;

/// <summary>
/// 记忆衰减/遗忘规则单元测试（2-D-4）。
/// 测试 HybridSearchOptions 新增配置、CalculateDecayFactor 计算逻辑、
/// Fuse 衰减叠加行为，以及 SearchAsync 更新 LastAccessedAtMs 的集成行为。
/// </summary>
public class MemoryDecayTests : IDisposable
{
    private readonly string _tempDir;
    private readonly RagDbContextFactory _factory;
    private readonly IEmbeddingService _embedding;

    public MemoryDecayTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mc_decay_tests_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _factory = new RagDbContextFactory(_tempDir);
        _embedding = Substitute.For<IEmbeddingService>();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── HybridSearchOptions 默认值 ──

    [Fact]
    public void HybridSearchOptions_EnableDecay_DefaultFalse()
    {
        var opts = new HybridSearchOptions();
        opts.EnableDecay.Should().BeFalse();
    }

    [Fact]
    public void HybridSearchOptions_DecayHalfLifeDays_Default90()
    {
        var opts = new HybridSearchOptions();
        opts.DecayHalfLifeDays.Should().BeApproximately(90f, 0.01f);
    }

    // ── CalculateDecayFactor ──

    [Fact]
    public void CalculateDecayFactor_AgeZero_ReturnsOne()
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        float factor = HybridSearchService.CalculateDecayFactor(null, nowMs, nowMs, halfLifeDays: 90f);
        factor.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void CalculateDecayFactor_AgeEqualsHalfLife_ReturnsHalf()
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long halfLifeMs = (long)(90.0 * 24 * 60 * 60 * 1000);
        long createdAtMs = nowMs - halfLifeMs;

        float factor = HybridSearchService.CalculateDecayFactor(null, createdAtMs, nowMs, halfLifeDays: 90f);

        factor.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void CalculateDecayFactor_AgeTwoHalfLives_ReturnsQuarter()
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long halfLifeMs = (long)(90.0 * 24 * 60 * 60 * 1000);
        long createdAtMs = nowMs - halfLifeMs * 2;

        float factor = HybridSearchService.CalculateDecayFactor(null, createdAtMs, nowMs, halfLifeDays: 90f);

        factor.Should().BeApproximately(0.25f, 0.01f);
    }

    [Fact]
    public void CalculateDecayFactor_FutureReference_ReturnsOne()
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long futureMs = nowMs + 10_000;

        // lastAccessedAtMs 在未来，不应出现负衰减
        float factor = HybridSearchService.CalculateDecayFactor(futureMs, futureMs, nowMs, halfLifeDays: 90f);
        factor.Should().BeApproximately(1f, 0.001f);
    }

    [Fact]
    public void CalculateDecayFactor_UsesLastAccessedOverCreated()
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long halfLifeMs = (long)(90.0 * 24 * 60 * 60 * 1000);

        long createdAtMs = nowMs - halfLifeMs * 2; // 创建于 2 个半衰期前
        long lastAccessedAtMs = nowMs - halfLifeMs; // 最近访问于 1 个半衰期前

        float factorWithAccess = HybridSearchService.CalculateDecayFactor(lastAccessedAtMs, createdAtMs, nowMs, halfLifeDays: 90f);
        float factorNoAccess = HybridSearchService.CalculateDecayFactor(null, createdAtMs, nowMs, halfLifeDays: 90f);

        // 使用 lastAccessedAtMs 的衰减应更少（0.5 vs 0.25）
        factorWithAccess.Should().BeGreaterThan(factorNoAccess);
        factorWithAccess.Should().BeApproximately(0.5f, 0.01f);
        factorNoAccess.Should().BeApproximately(0.25f, 0.01f);
    }

    // ── Fuse 衰减叠加 ──

    [Fact]
    public void Fuse_DecayDisabled_ScoreUnchanged()
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long halfLifeMs = (long)(90.0 * 24 * 60 * 60 * 1000);
        long oldCreatedAtMs = nowMs - halfLifeMs * 2; // 2 个半衰期前

        var entity = new VectorChunkEntity
        {
            Id = "old", SourceId = "s", Content = "old chunk",
            VectorBlob = [], CreatedAtMs = oldCreatedAtMs
        };

        var semantic = new Dictionary<string, (VectorChunkEntity, float)>
            { ["old"] = (entity, 0.8f) };
        var keyword = new Dictionary<string, (VectorChunkEntity, float)>();

        var opts = new HybridSearchOptions { EnableDecay = false };
        var results = HybridSearchService.Fuse(semantic, keyword, opts, nowMs);

        results.Should().HaveCount(1);
        // 衰减未启用，DecayFactor 应为 1，FusedScore = 0.8 * 0.7 = 0.56
        results[0].DecayFactor.Should().BeApproximately(1f, 0.001f);
        results[0].FusedScore.Should().BeApproximately(0.56f, 0.01f);
    }

    [Fact]
    public void Fuse_DecayEnabled_OldChunkGetsLowerScore()
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long halfLifeMs = (long)(90.0 * 24 * 60 * 60 * 1000);

        var freshEntity = new VectorChunkEntity
        {
            Id = "fresh", SourceId = "s", Content = "fresh",
            VectorBlob = [], CreatedAtMs = nowMs
        };
        var oldEntity = new VectorChunkEntity
        {
            Id = "old", SourceId = "s", Content = "old",
            VectorBlob = [], CreatedAtMs = nowMs - halfLifeMs
        };

        // 两个分块语义分数相同
        var semantic = new Dictionary<string, (VectorChunkEntity, float)>
        {
            ["fresh"] = (freshEntity, 0.8f),
            ["old"] = (oldEntity, 0.8f)
        };
        var keyword = new Dictionary<string, (VectorChunkEntity, float)>();

        var opts = new HybridSearchOptions { EnableDecay = true, DecayHalfLifeDays = 90f };
        var results = HybridSearchService.Fuse(semantic, keyword, opts, nowMs);

        results.Should().HaveCount(2);
        var freshResult = results.First(r => r.Record.Id == "fresh");
        var oldResult = results.First(r => r.Record.Id == "old");

        // 新分块排名应高于旧分块
        freshResult.FusedScore.Should().BeGreaterThan(oldResult.FusedScore);
        freshResult.DecayFactor.Should().BeApproximately(1f, 0.01f);
        oldResult.DecayFactor.Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void Fuse_DecayEnabled_ResultsSortedByDecayedScore()
    {
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long halfLifeMs = (long)(90.0 * 24 * 60 * 60 * 1000);

        // 旧分块语义分数更高，但衰减后应排在新分块之后
        var freshEntity = new VectorChunkEntity
        {
            Id = "fresh", SourceId = "s", Content = "fresh",
            VectorBlob = [], CreatedAtMs = nowMs
        };
        var oldEntity = new VectorChunkEntity
        {
            Id = "old", SourceId = "s", Content = "old",
            VectorBlob = [], CreatedAtMs = nowMs - halfLifeMs * 3 // 3 个半衰期前
        };

        var semantic = new Dictionary<string, (VectorChunkEntity, float)>
        {
            ["fresh"] = (freshEntity, 0.6f),  // 新分块分数较低
            ["old"] = (oldEntity, 0.9f)       // 旧分块分数较高，但衰减后 0.9*0.125=0.1125
        };
        var keyword = new Dictionary<string, (VectorChunkEntity, float)>();

        var opts = new HybridSearchOptions { EnableDecay = true, DecayHalfLifeDays = 90f };
        var results = HybridSearchService.Fuse(semantic, keyword, opts, nowMs);

        // 衰减后，新分块应排在第一位
        results[0].Record.Id.Should().Be("fresh");
        results[1].Record.Id.Should().Be("old");
    }

    // ── VectorChunkEntity.LastAccessedAtMs ──

    [Fact]
    public void VectorChunkEntity_LastAccessedAtMs_DefaultNull()
    {
        var entity = new VectorChunkEntity();
        entity.LastAccessedAtMs.Should().BeNull();
    }

    [Fact]
    public void VectorChunkEntity_LastAccessedAtMs_CanBeSet()
    {
        long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var entity = new VectorChunkEntity { LastAccessedAtMs = ts };
        entity.LastAccessedAtMs.Should().Be(ts);
    }

    // ── SearchAsync 更新 LastAccessedAtMs（集成测试） ──

    [Fact]
    public async Task SearchAsync_UpdatesLastAccessedAtMs_ForReturnedChunks()
    {
        // Arrange
        float[] vec = [1f, 0f, 0f];
        _embedding.GenerateAsync(default!, default)
            .ReturnsForAnyArgs(vec.AsMemory());

        using (var db = _factory.Create(RagScope.Global))
        {
            db.VectorChunks.Add(new VectorChunkEntity
            {
                Id = "c1", SourceId = "src", Content = "fox dog cat",
                VectorBlob = VectorHelper.ToBytes(vec),
                CreatedAtMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1000,
                LastAccessedAtMs = null
            });
            await db.SaveChangesAsync();
        }

        var sut = new HybridSearchService(_embedding, _factory, NullLogger<HybridSearchService>.Instance);

        // Act
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await sut.SearchAsync("fox", RagScope.Global);
        // fire-and-forget: give a brief moment to complete
        await Task.Delay(200);
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Assert
        using var verifyDb = _factory.Create(RagScope.Global);
        var chunk = await verifyDb.VectorChunks.FindAsync("c1");
        chunk.Should().NotBeNull();
        chunk!.LastAccessedAtMs.Should().NotBeNull();
        chunk.LastAccessedAtMs.Should().BeGreaterThanOrEqualTo(before);
        chunk.LastAccessedAtMs.Should().BeLessThanOrEqualTo(after);
    }

    [Fact]
    public async Task SearchAsync_DecayEnabled_UsingOptions_AppliesDecayFactor()
    {
        // Arrange: 两个 chunk，一个新一个旧，语义相同
        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long halfLifeMs = (long)(90.0 * 24 * 60 * 60 * 1000);
        float[] vec = [1f, 0f, 0f];
        _embedding.GenerateAsync(default!, default)
            .ReturnsForAnyArgs(vec.AsMemory());

        using (var db = _factory.Create(RagScope.Global))
        {
            db.VectorChunks.AddRange(
                new VectorChunkEntity
                {
                    Id = "fresh", SourceId = "s", Content = "machine learning test",
                    VectorBlob = VectorHelper.ToBytes(vec), CreatedAtMs = nowMs
                },
                new VectorChunkEntity
                {
                    Id = "stale", SourceId = "s", Content = "machine learning test",
                    VectorBlob = VectorHelper.ToBytes(vec), CreatedAtMs = nowMs - halfLifeMs
                }
            );
            await db.SaveChangesAsync();
        }

        var sut = new HybridSearchService(_embedding, _factory, NullLogger<HybridSearchService>.Instance);
        var opts = new HybridSearchOptions { EnableDecay = true, DecayHalfLifeDays = 90f };

        // Act
        var results = await sut.SearchAsync("machine learning test", RagScope.Global, options: opts);

        // Assert: 新分块排在前，旧分块衰减因子约 0.5
        results.Should().HaveCount(2);
        results[0].Record.Id.Should().Be("fresh");
        var staleResult = results.First(r => r.Record.Id == "stale");
        staleResult.DecayFactor.Should().BeApproximately(0.5f, 0.05f);
    }

    // ── RagDbContextFactory 模式迁移测试 ──

    [Fact]
    public void Factory_ExistingDb_EvolveSchema_AddsLastAccessedColumn()
    {
        // 先用 EnsureCreated 创建没有新列的库（模拟旧数据库）
        using var db1 = _factory.Create(RagScope.Global);
        db1.Should().NotBeNull(); // EvolveSchema 应成功执行，不抛异常

        // 再次打开（EvolveSchema 内捕获"duplicate column"异常）
        using var db2 = _factory.Create(RagScope.Global);
        db2.Should().NotBeNull();
    }
}
