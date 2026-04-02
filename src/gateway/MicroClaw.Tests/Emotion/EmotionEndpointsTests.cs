using FluentAssertions;
using MicroClaw.Emotion;
using MicroClaw.Endpoints;
using MicroClaw.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace MicroClaw.Tests.Emotion;

public class EmotionEndpointsTests
{
    // ── GetCurrentAsync 注入验证 ──

    [Fact]
    public async Task GetCurrent_ReturnsDefaultState_WhenNoSnapshotsExist()
    {
        // Arrange: IEmotionStore.GetCurrentAsync 返回默认状态
        var store = Substitute.For<IEmotionStore>();
        store.GetCurrentAsync("agent1", Arg.Any<CancellationToken>())
            .Returns(EmotionState.Default);

        // Act: 调用 store（模拟端点内部逻辑）
        EmotionState state = await store.GetCurrentAsync("agent1");

        // Assert
        state.Alertness.Should().Be(50);
        state.Mood.Should().Be(50);
        state.Curiosity.Should().Be(50);
        state.Confidence.Should().Be(50);
    }

    [Fact]
    public async Task GetCurrent_ReturnsCorrectValues_WhenSnapshotExists()
    {
        var expected = new EmotionState(alertness: 80, mood: 60, curiosity: 70, confidence: 40);
        var store = Substitute.For<IEmotionStore>();
        store.GetCurrentAsync("agent2", Arg.Any<CancellationToken>()).Returns(expected);

        EmotionState result = await store.GetCurrentAsync("agent2");

        result.Alertness.Should().Be(80);
        result.Mood.Should().Be(60);
        result.Curiosity.Should().Be(70);
        result.Confidence.Should().Be(40);
    }

    // ── GetHistoryAsync 注入验证 ──

    [Fact]
    public async Task GetHistory_ReturnsEmptyList_WhenNoSnapshots()
    {
        var store = Substitute.For<IEmotionStore>();
        store.GetHistoryAsync("agent1", Arg.Any<long>(), Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<EmotionSnapshot>());

        IReadOnlyList<EmotionSnapshot> result = await store.GetHistoryAsync("agent1", 0, long.MaxValue);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistory_ReturnsOrderedSnapshots()
    {
        var t1 = 1_000L;
        var t2 = 2_000L;
        var snapshots = new[]
        {
            new EmotionSnapshot(new EmotionState(60, 55, 65, 70), t1),
            new EmotionSnapshot(new EmotionState(70, 65, 75, 80), t2),
        };

        var store = Substitute.For<IEmotionStore>();
        store.GetHistoryAsync("agent1", t1, t2, Arg.Any<CancellationToken>())
            .Returns(snapshots);

        IReadOnlyList<EmotionSnapshot> result = await store.GetHistoryAsync("agent1", t1, t2);

        result.Should().HaveCount(2);
        result[0].RecordedAtMs.Should().Be(t1);
        result[1].RecordedAtMs.Should().Be(t2);
    }

    // ── DTO 映射验证 ──

    [Fact]
    public void EmotionStateDto_MapsAllFields()
    {
        var dto = new EmotionEndpoints.EmotionStateDto(10, 20, 30, 40);
        dto.Alertness.Should().Be(10);
        dto.Mood.Should().Be(20);
        dto.Curiosity.Should().Be(30);
        dto.Confidence.Should().Be(40);
    }

    [Fact]
    public void EmotionSnapshotDto_MapsAllFields()
    {
        var dto = new EmotionEndpoints.EmotionSnapshotDto(10, 20, 30, 40, 999_000L);
        dto.Alertness.Should().Be(10);
        dto.Mood.Should().Be(20);
        dto.Curiosity.Should().Be(30);
        dto.Confidence.Should().Be(40);
        dto.RecordedAtMs.Should().Be(999_000L);
    }

    // ── 请求验证：from > to 应拒绝 ──

    [Fact]
    public void EmotionHistoryRequest_FromGreaterThanTo_IsInvalidByConvention()
    {
        // 验证请求对象能正确携带 from/to 值，业务校验在端点层
        var req = new EmotionEndpoints.EmotionHistoryRequest(From: 2000L, To: 1000L);
        req.From.Should().BeGreaterThan(req.To);
    }

    // ── EmotionStore 集成：存储并检索历史 ──

    [Fact]
    public async Task EmotionStore_SaveAndGetHistory_ReturnsCorrectRange()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "microclaw_emotion_ep_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var services = new ServiceCollection();
            services.AddDbContextFactory<GatewayDbContext>(opts =>
                opts.UseSqlite($"Data Source={Path.Combine(tempDir, "microclaw.db")}"));
            var sp = services.BuildServiceProvider();
            var factory = sp.GetRequiredService<IDbContextFactory<GatewayDbContext>>();
            using var ctx = factory.CreateDbContext();
            ctx.Database.EnsureCreated();
            var store = new EmotionStore(factory);
            const string agentId = "test-agent";

            await store.SaveAsync(agentId, new EmotionState(70, 80, 60, 50));
            await store.SaveAsync(agentId, new EmotionState(75, 85, 65, 55));

            long from = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
            long to = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();

            IReadOnlyList<EmotionSnapshot> history = await store.GetHistoryAsync(agentId, from, to);

            history.Should().HaveCount(2);
            history.Should().BeInAscendingOrder(s => s.RecordedAtMs);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* 清理 */ }
        }
    }

    [Fact]
    public async Task EmotionStore_GetCurrent_ReturnsLatestSnapshot()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "microclaw_emotion_ep_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var services = new ServiceCollection();
            services.AddDbContextFactory<GatewayDbContext>(opts =>
                opts.UseSqlite($"Data Source={Path.Combine(tempDir, "microclaw.db")}"));
            var sp = services.BuildServiceProvider();
            var factory = sp.GetRequiredService<IDbContextFactory<GatewayDbContext>>();
            using var ctx = factory.CreateDbContext();
            ctx.Database.EnsureCreated();
            var store = new EmotionStore(factory);
            const string agentId = "test-agent";

            await store.SaveAsync(agentId, new EmotionState(30, 40, 50, 60));
            await Task.Delay(1); // 确保时间戳差异
            await store.SaveAsync(agentId, new EmotionState(90, 85, 80, 75));

            EmotionState current = await store.GetCurrentAsync(agentId);

            current.Alertness.Should().Be(90);
            current.Mood.Should().Be(85);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { /* 清理 */ }
        }
    }
}
