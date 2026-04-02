using FluentAssertions;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Safety;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace MicroClaw.Tests.Safety;

public class PainMemoryStoreTests : IDisposable
{
    private readonly TestGatewayDbContextFactoryForSafety _factory;
    private readonly PainMemoryStore _store;
    private readonly string _tempDir;

    public PainMemoryStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "microclaw_safety_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _factory = new TestGatewayDbContextFactoryForSafety(_tempDir);
        _store = new PainMemoryStore(_factory);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 清理失败静默忽略 */ }
    }

    // ── 构造参数校验 ───────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullFactory_Throws()
    {
        var act = () => new PainMemoryStore(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── RecordAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordAsync_NullMemory_Throws()
    {
        var act = () => _store.RecordAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RecordAsync_ValidMemory_ReturnsSavedMemory()
    {
        var memory = PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.High);

        var saved = await _store.RecordAsync(memory);

        saved.Should().NotBeNull();
        saved.Id.Should().Be(memory.Id);
        saved.AgentId.Should().Be("agent1");
        saved.TriggerDescription.Should().Be("trigger");
        saved.Severity.Should().Be(PainSeverity.High);
        saved.OccurrenceCount.Should().Be(1);
    }

    [Fact]
    public async Task RecordAsync_TwoMemories_BothPersisted()
    {
        var m1 = PainMemory.Create("agent1", "trigger-1", "consequence-1", "strategy-1", PainSeverity.Low);
        var m2 = PainMemory.Create("agent1", "trigger-2", "consequence-2", "strategy-2", PainSeverity.Critical);

        await _store.RecordAsync(m1);
        await _store.RecordAsync(m2);

        var all = await _store.GetAllAsync("agent1");
        all.Should().HaveCount(2);
    }

    // ── GetAllAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_EmptyStore_ReturnsEmptyList()
    {
        var result = await _store.GetAllAsync("agent-no-memories");
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_NullAgentId_Throws()
    {
        var act = () => _store.GetAllAsync(null!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsOnlyOwnAgentMemories()
    {
        await _store.RecordAsync(PainMemory.Create("agent1", "t1", "c1", "s1", PainSeverity.Low));
        await _store.RecordAsync(PainMemory.Create("agent2", "t2", "c2", "s2", PainSeverity.High));

        var agent1Memories = await _store.GetAllAsync("agent1");
        agent1Memories.Should().HaveCount(1);
        agent1Memories[0].AgentId.Should().Be("agent1");
    }

    [Fact]
    public async Task GetAllAsync_OrderedBySeverityDescThenOccurrenceDesc()
    {
        await _store.RecordAsync(PainMemory.Create("agent1", "low-trigger", "c", "s", PainSeverity.Low));
        await _store.RecordAsync(PainMemory.Create("agent1", "critical-trigger", "c", "s", PainSeverity.Critical));
        await _store.RecordAsync(PainMemory.Create("agent1", "high-trigger", "c", "s", PainSeverity.High));

        var result = await _store.GetAllAsync("agent1");

        result.Should().HaveCount(3);
        result[0].Severity.Should().Be(PainSeverity.Critical);
        result[1].Severity.Should().Be(PainSeverity.High);
        result[2].Severity.Should().Be(PainSeverity.Low);
    }

    [Fact]
    public async Task GetAllAsync_SameSeverity_OrderedByOccurrenceCountDesc()
    {
        var m1 = await _store.RecordAsync(PainMemory.Create("agent1", "trigger-a", "c", "s", PainSeverity.Medium));
        var m2 = await _store.RecordAsync(PainMemory.Create("agent1", "trigger-b", "c", "s", PainSeverity.Medium));

        // Give m2 more occurrences
        await _store.IncrementOccurrenceAsync("agent1", m2.Id);
        await _store.IncrementOccurrenceAsync("agent1", m2.Id);

        var result = await _store.GetAllAsync("agent1");

        result.Should().HaveCount(2);
        result[0].TriggerDescription.Should().Be("trigger-b");
        result[1].TriggerDescription.Should().Be("trigger-a");
    }

    // ── IncrementOccurrenceAsync ───────────────────────────────────────────────

    [Fact]
    public async Task IncrementOccurrenceAsync_NullAgentId_Throws()
    {
        var act = () => _store.IncrementOccurrenceAsync(null!, "id");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task IncrementOccurrenceAsync_NullPainMemoryId_Throws()
    {
        var act = () => _store.IncrementOccurrenceAsync("agent1", null!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task IncrementOccurrenceAsync_NotExisting_ReturnsNull()
    {
        var result = await _store.IncrementOccurrenceAsync("agent1", "non-existent-id");
        result.Should().BeNull();
    }

    [Fact]
    public async Task IncrementOccurrenceAsync_WrongAgent_ReturnsNull()
    {
        var memory = await _store.RecordAsync(
            PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.Low));

        var result = await _store.IncrementOccurrenceAsync("agent2", memory.Id);
        result.Should().BeNull();
    }

    [Fact]
    public async Task IncrementOccurrenceAsync_ValidId_IncrementsCount()
    {
        var memory = await _store.RecordAsync(
            PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.Low));

        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var updated = await _store.IncrementOccurrenceAsync("agent1", memory.Id);
        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        updated.Should().NotBeNull();
        updated!.OccurrenceCount.Should().Be(2);
        updated.LastOccurredAtMs.Should().BeInRange(before, after);
    }

    [Fact]
    public async Task IncrementOccurrenceAsync_CalledThreeTimes_OccurrenceCountIsFour()
    {
        var memory = await _store.RecordAsync(
            PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.High));

        await _store.IncrementOccurrenceAsync("agent1", memory.Id);
        await _store.IncrementOccurrenceAsync("agent1", memory.Id);
        var final = await _store.IncrementOccurrenceAsync("agent1", memory.Id);

        final!.OccurrenceCount.Should().Be(4);
    }

    [Fact]
    public async Task IncrementOccurrenceAsync_PreservesOtherFields()
    {
        var memory = await _store.RecordAsync(
            PainMemory.Create("agent1", "特定触发点", "严重后果", "规避策略", PainSeverity.Critical));

        var updated = await _store.IncrementOccurrenceAsync("agent1", memory.Id);

        updated!.TriggerDescription.Should().Be("特定触发点");
        updated.ConsequenceDescription.Should().Be("严重后果");
        updated.AvoidanceStrategy.Should().Be("规避策略");
        updated.Severity.Should().Be(PainSeverity.Critical);
        updated.CreatedAtMs.Should().Be(memory.CreatedAtMs);
    }

    // ── DeleteAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_NullAgentId_Throws()
    {
        var act = () => _store.DeleteAsync(null!, "id");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DeleteAsync_NullPainMemoryId_Throws()
    {
        var act = () => _store.DeleteAsync("agent1", null!);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DeleteAsync_NotExisting_SilentlyIgnores()
    {
        var act = () => _store.DeleteAsync("agent1", "non-existent-id");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_WrongAgent_DoesNotDelete()
    {
        var memory = await _store.RecordAsync(
            PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.Low));

        await _store.DeleteAsync("agent2", memory.Id);

        var remaining = await _store.GetAllAsync("agent1");
        remaining.Should().HaveCount(1);
    }

    [Fact]
    public async Task DeleteAsync_ValidId_RemovesMemory()
    {
        var memory = await _store.RecordAsync(
            PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.Low));

        await _store.DeleteAsync("agent1", memory.Id);

        var remaining = await _store.GetAllAsync("agent1");
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_OnlyDeletesTargetMemory()
    {
        var m1 = await _store.RecordAsync(PainMemory.Create("agent1", "t1", "c1", "s1", PainSeverity.Low));
        var m2 = await _store.RecordAsync(PainMemory.Create("agent1", "t2", "c2", "s2", PainSeverity.High));

        await _store.DeleteAsync("agent1", m1.Id);

        var remaining = await _store.GetAllAsync("agent1");
        remaining.Should().HaveCount(1);
        remaining[0].Id.Should().Be(m2.Id);
    }

    // ── 痛觉-情绪联动集成 ──────────────────────────────────────────────────────

    [Fact]
    public async Task RecordAsync_HighSeverity_CallsEmotionLinker()
    {
        var linker = NSubstitute.Substitute.For<IPainEmotionLinker>();
        var storeWithLinker = new PainMemoryStore(_factory, linker);
        var memory = PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.High);

        var saved = await storeWithLinker.RecordAsync(memory);

        await linker.Received(1).LinkAsync(saved, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_CriticalSeverity_CallsEmotionLinker()
    {
        var linker = NSubstitute.Substitute.For<IPainEmotionLinker>();
        var storeWithLinker = new PainMemoryStore(_factory, linker);
        var memory = PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.Critical);

        var saved = await storeWithLinker.RecordAsync(memory);

        await linker.Received(1).LinkAsync(saved, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_LowSeverity_WithLinker_StillSavesMemory()
    {
        var linker = NSubstitute.Substitute.For<IPainEmotionLinker>();
        var storeWithLinker = new PainMemoryStore(_factory, linker);
        var memory = PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.Low);

        var saved = await storeWithLinker.RecordAsync(memory);

        saved.Should().NotBeNull();
        // linker 仍会被调用（由 linker 自行决定是否处理 Low 严重度）
        await linker.Received(1).LinkAsync(saved, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordAsync_WithoutLinker_StillSavesMemoryNormally()
    {
        // 无 linker 时（默认构造）不应抛出，且记忆正常保存
        var memory = PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.Critical);

        var saved = await _store.RecordAsync(memory);

        saved.Should().NotBeNull();
        saved.AgentId.Should().Be("agent1");
    }
}

/// <summary>测试用 GatewayDbContext 工厂，使用 SQLite 文件数据库。</summary>
internal sealed class TestGatewayDbContextFactoryForSafety : IDbContextFactory<GatewayDbContext>
{
    private readonly DbContextOptions<GatewayDbContext> _opts;

    public TestGatewayDbContextFactoryForSafety(string tempDir)
    {
        _opts = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseSqlite($"Data Source={Path.Combine(tempDir, "test.db")}")
            .Options;
        using var ctx = new GatewayDbContext(_opts);
        ctx.Database.EnsureCreated();
    }

    public GatewayDbContext CreateDbContext() => new(_opts);
}
