using FluentAssertions;
using MicroClaw.Pet.Emotion;

namespace MicroClaw.Tests.Emotion;

public class EmotionStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly EmotionStore _store;

    public EmotionStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "microclaw_pet_emotion_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _store = new EmotionStore(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* 清理错误忽略 */ }
    }

    // ── 构造函数参数验证 ──

    [Fact]
    public void Constructor_NullSessionsDir_Throws()
    {
        var act = () => new EmotionStore((string)null!);
        act.Should().Throw<ArgumentException>();
    }

    // ── GetCurrentAsync：无记录返回默认值 ──

    [Fact]
    public async Task GetCurrentAsync_NoRecords_ReturnsDefault()
    {
        var state = await _store.GetCurrentAsync("session-a");
        state.Should().Be(EmotionState.Default);
    }

    // ── SaveAsync 参数验证 ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveAsync_InvalidSessionId_Throws(string? sessionId)
    {
        var act = async () => await _store.SaveAsync(sessionId!, EmotionState.Default);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SaveAsync_NullState_Throws()
    {
        var act = async () => await _store.SaveAsync("session-a", null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── SaveAsync + GetCurrentAsync ──

    [Fact]
    public async Task SaveThenGetCurrent_ReturnsSavedState()
    {
        var expected = new EmotionState(alertness: 80, mood: 60, curiosity: 40, confidence: 70);
        await _store.SaveAsync("session-a", expected);

        var actual = await _store.GetCurrentAsync("session-a");

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

        await _store.SaveAsync("session-a", first);
        await _store.SaveAsync("session-a", latest);

        var actual = await _store.GetCurrentAsync("session-a");

        actual.Alertness.Should().Be(latest.Alertness);
        actual.Mood.Should().Be(latest.Mood);
    }

    // ── Session 隔离 ──

    [Fact]
    public async Task GetCurrentAsync_DifferentSessions_AreIsolated()
    {
        var stateA = new EmotionState(alertness: 20, mood: 20, curiosity: 20, confidence: 20);
        var stateB = new EmotionState(alertness: 80, mood: 80, curiosity: 80, confidence: 80);

        await _store.SaveAsync("session-a", stateA);
        await _store.SaveAsync("session-b", stateB);

        var actualA = await _store.GetCurrentAsync("session-a");
        var actualB = await _store.GetCurrentAsync("session-b");

        actualA.Alertness.Should().Be(20);
        actualB.Alertness.Should().Be(80);
    }

    [Fact]
    public async Task GetCurrentAsync_SessionWithNoSaves_UnaffectedByOtherSession()
    {
        await _store.SaveAsync("session-a", new EmotionState(mood: 99));

        var actual = await _store.GetCurrentAsync("session-b");
        actual.Should().Be(EmotionState.Default);
    }

    // ── EmotionSnapshot record ──

    [Fact]
    public void EmotionSnapshot_CorrectlyWrapsStateAndTimestamp()
    {
        var state = new EmotionState(alertness: 70, mood: 60, curiosity: 50, confidence: 40);
        var snapshot = new EmotionSnapshot(state, 12345L);

        snapshot.State.Should().Be(state);
        snapshot.RecordedAtMs.Should().Be(12345L);
    }
}
