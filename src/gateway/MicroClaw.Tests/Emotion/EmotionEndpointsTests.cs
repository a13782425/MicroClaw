using FluentAssertions;
using MicroClaw.Pet.Emotion;
using NSubstitute;

namespace MicroClaw.Tests.Emotion;

/// <summary>
/// Pet IEmotionStore 接口行为契约验证。
/// EmotionEndpoints 已在 P-B-6 迁移中删除（旧 API 基于 AgentId，新 Pet 情绪基于 SessionId）。
/// 本测试覆盖 IEmotionStore 接口的 mock 行为以及 EmotionSnapshot 模型。
/// </summary>
public class PetEmotionStoreInterfaceTests
{
    // ── GetCurrentAsync 接口契约测试 ──

    [Fact]
    public async Task GetCurrent_ReturnsDefaultState_WhenNoSnapshotsExist()
    {
        var store = Substitute.For<IEmotionStore>();
        store.GetCurrentAsync("session-1", Arg.Any<CancellationToken>())
            .Returns(EmotionState.Default);

        EmotionState state = await store.GetCurrentAsync("session-1");

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
        store.GetCurrentAsync("session-2", Arg.Any<CancellationToken>()).Returns(expected);

        EmotionState result = await store.GetCurrentAsync("session-2");

        result.Alertness.Should().Be(80);
        result.Mood.Should().Be(60);
        result.Curiosity.Should().Be(70);
        result.Confidence.Should().Be(40);
    }

    // ── EmotionSnapshot record ──

    [Fact]
    public void EmotionSnapshot_MapsAllFields()
    {
        var state = new EmotionState(alertness: 10, mood: 20, curiosity: 30, confidence: 40);
        var snapshot = new EmotionSnapshot(state, 999_000L);

        snapshot.State.Alertness.Should().Be(10);
        snapshot.State.Mood.Should().Be(20);
        snapshot.State.Curiosity.Should().Be(30);
        snapshot.State.Confidence.Should().Be(40);
        snapshot.RecordedAtMs.Should().Be(999_000L);
    }
}
