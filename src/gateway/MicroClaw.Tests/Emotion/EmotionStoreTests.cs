using FluentAssertions;
using MicroClaw.Emotion;
using MicroClaw.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MicroClaw.Tests.Emotion;

public class EmotionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IDbContextFactory<GatewayDbContext> _factory;
    private readonly EmotionStore _store;

    public EmotionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "microclaw_emotion_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var services = new ServiceCollection();
        services.AddDbContextFactory<GatewayDbContext>(opts =>
            opts.UseSqlite($"Data Source={Path.Combine(_tempDir, "microclaw.db")}"));
        var provider = services.BuildServiceProvider();
        _factory = provider.GetRequiredService<IDbContextFactory<GatewayDbContext>>();

        using var ctx = _factory.CreateDbContext();
        ctx.Database.EnsureCreated();

        _store = new EmotionStore(_factory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 娓呯悊锛屽拷鐣?*/ }
    }

    // 鈹€鈹€ 鏋勯€犲嚱鏁板弬鏁伴獙璇?鈹€鈹€

    [Fact]
    public void Constructor_NullFactory_Throws()
    {
        var act = () => new EmotionStore(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // 鈹€鈹€ GetCurrentAsync锛氭棤璁板綍杩斿洖榛樿鍊?鈹€鈹€

    [Fact]
    public async Task GetCurrentAsync_NoRecords_ReturnsDefault()
    {
        var state = await _store.GetCurrentAsync("agent-a");
        state.Should().Be(EmotionState.Default);
    }

    // 鈹€鈹€ SaveAsync 鍙傛暟楠岃瘉 鈹€鈹€

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAsync_InvalidAgentId_Throws(string? agentId)
    {
        var act = async () => await _store.SaveAsync(agentId!, EmotionState.Default);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SaveAsync_NullState_Throws()
    {
        var act = async () => await _store.SaveAsync("agent-a", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // 鈹€鈹€ SaveAsync + GetCurrentAsync 鈹€鈹€

    [Fact]
    public async Task SaveThenGetCurrent_ReturnsSavedState()
    {
        var expected = new EmotionState(alertness: 80, mood: 60, curiosity: 40, confidence: 70);
        await _store.SaveAsync("agent-a", expected);

        var actual = await _store.GetCurrentAsync("agent-a");

        actual.Alertness.Should().Be(expected.Alertness);
        actual.Mood.Should().Be(expected.Mood);
        actual.Curiosity.Should().Be(expected.Curiosity);
        actual.Confidence.Should().Be(expected.Confidence);
    }

    [Fact]
    public async Task SaveMultipleTimes_GetCurrentReturnsLatest()
    {
        var first = new EmotionState(alertness: 30, mood: 30, curiosity: 30, confidence: 30);
        var latest = new EmotionState(alertness: 90, mood: 90, curiosity: 90, confidence: 90);

        await _store.SaveAsync("agent-a", first);
        await _store.SaveAsync("agent-a", latest);

        var actual = await _store.GetCurrentAsync("agent-a");

        actual.Alertness.Should().Be(latest.Alertness);
        actual.Mood.Should().Be(latest.Mood);
    }

    // 鈹€鈹€ Agent 闅旂 鈹€鈹€

    [Fact]
    public async Task GetCurrentAsync_DifferentAgents_AreIsolated()
    {
        var stateA = new EmotionState(alertness: 20, mood: 20, curiosity: 20, confidence: 20);
        var stateB = new EmotionState(alertness: 80, mood: 80, curiosity: 80, confidence: 80);

        await _store.SaveAsync("agent-a", stateA);
        await _store.SaveAsync("agent-b", stateB);

        var actualA = await _store.GetCurrentAsync("agent-a");
        var actualB = await _store.GetCurrentAsync("agent-b");

        actualA.Alertness.Should().Be(20);
        actualB.Alertness.Should().Be(80);
    }

    [Fact]
    public async Task GetCurrentAsync_AgentWithNoSaves_UnaffectedByOtherAgent()
    {
        await _store.SaveAsync("agent-a", new EmotionState(mood: 99));

        var actual = await _store.GetCurrentAsync("agent-b");
        actual.Should().Be(EmotionState.Default);
    }

    // 鈹€鈹€ GetHistoryAsync 鈹€鈹€

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetHistoryAsync_InvalidAgentId_Throws(string? agentId)
    {
        var act = async () => await _store.GetHistoryAsync(agentId!, from: 0, to: long.MaxValue);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetHistoryAsync_NoRecords_ReturnsEmpty()
    {
        var history = await _store.GetHistoryAsync("agent-a", from: 0, to: long.MaxValue);
        history.Should().BeEmpty();
    }

    [Fact]
    public async Task GetHistoryAsync_ReturnsRecordsInAscendingOrder()
    {
        var state1 = new EmotionState(alertness: 10);
        var state2 = new EmotionState(alertness: 50);
        var state3 = new EmotionState(alertness: 90);

        await _store.SaveAsync("agent-a", state1);
        await Task.Delay(5); // 确保时间戳不同
        await _store.SaveAsync("agent-a", state2);
        await Task.Delay(5);
        await _store.SaveAsync("agent-a", state3);

        var history = await _store.GetHistoryAsync("agent-a", from: 0, to: long.MaxValue);

        history.Should().HaveCount(3);
        history[0].State.Alertness.Should().Be(10);
        history[1].State.Alertness.Should().Be(50);
        history[2].State.Alertness.Should().Be(90);
    }

    [Fact]
    public async Task GetHistoryAsync_TimeRangeFilter_OnlyReturnsInRange()
    {
        long before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _store.SaveAsync("agent-a", new EmotionState(alertness: 30));
        await Task.Delay(10);
        long mid = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _store.SaveAsync("agent-a", new EmotionState(alertness: 60));
        await Task.Delay(10);
        long after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _store.SaveAsync("agent-a", new EmotionState(alertness: 90));

        // 只查 mid 到 after 之间的记录（即第二条）
        var history = await _store.GetHistoryAsync("agent-a", from: mid, to: after - 1);

        history.Should().HaveCount(1);
        history[0].State.Alertness.Should().Be(60);
    }

    [Fact]
    public async Task GetHistoryAsync_SnapshotHasCorrectTimestamp()
    {
        long beforeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _store.SaveAsync("agent-a", EmotionState.Default);
        long afterMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var history = await _store.GetHistoryAsync("agent-a", from: 0, to: long.MaxValue);

        history.Should().HaveCount(1);
        history[0].RecordedAtMs.Should().BeInRange(beforeMs, afterMs);
    }

    [Fact]
    public async Task GetHistoryAsync_DifferentAgents_AreIsolated()
    {
        await _store.SaveAsync("agent-a", new EmotionState(alertness: 10));
        await _store.SaveAsync("agent-b", new EmotionState(alertness: 20));
        await _store.SaveAsync("agent-a", new EmotionState(alertness: 30));

        var historyA = await _store.GetHistoryAsync("agent-a", from: 0, to: long.MaxValue);
        var historyB = await _store.GetHistoryAsync("agent-b", from: 0, to: long.MaxValue);

        historyA.Should().HaveCount(2);
        historyB.Should().HaveCount(1);
        historyB[0].State.Alertness.Should().Be(20);
    }

    // 鈹€鈹€ EmotionSnapshot record 鈹€鈹€

    [Fact]
    public void EmotionSnapshot_CorrectlyWrapsStateAndTimestamp()
    {
        var state = new EmotionState(alertness: 70, mood: 60, curiosity: 50, confidence: 40);
        var snapshot = new EmotionSnapshot(state, 12345L);

        snapshot.State.Should().Be(state);
        snapshot.RecordedAtMs.Should().Be(12345L);
    }
}

