using FluentAssertions;
using MicroClaw.Pet.Emotion;

namespace MicroClaw.Tests.Emotion;

public class EmotionStateTests
{
    // ── 默认值 ──

    [Fact]
    public void Default_AllDimensionsAreFifty()
    {
        var state = EmotionState.Default;

        state.Alertness.Should().Be(50);
        state.Mood.Should().Be(50);
        state.Curiosity.Should().Be(50);
        state.Confidence.Should().Be(50);
    }

    [Fact]
    public void DefaultCtor_AllDimensionsAreFifty()
    {
        var state = new EmotionState();

        state.Alertness.Should().Be(EmotionState.DefaultValue);
        state.Mood.Should().Be(EmotionState.DefaultValue);
        state.Curiosity.Should().Be(EmotionState.DefaultValue);
        state.Confidence.Should().Be(EmotionState.DefaultValue);
    }

    // ── 构造时值域验证（Clamp） ──

    [Fact]
    public void Ctor_ValuesAbove100_ClampedTo100()
    {
        var state = new EmotionState(alertness: 200, mood: 150, curiosity: 101, confidence: 999);

        state.Alertness.Should().Be(100);
        state.Mood.Should().Be(100);
        state.Curiosity.Should().Be(100);
        state.Confidence.Should().Be(100);
    }

    [Fact]
    public void Ctor_ValuesBelow0_ClampedTo0()
    {
        var state = new EmotionState(alertness: -1, mood: -50, curiosity: -100, confidence: int.MinValue);

        state.Alertness.Should().Be(0);
        state.Mood.Should().Be(0);
        state.Curiosity.Should().Be(0);
        state.Confidence.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(99)]
    [InlineData(100)]
    public void Ctor_ValidBoundaryValues_StoredExactly(int value)
    {
        var state = new EmotionState(alertness: value, mood: value, curiosity: value, confidence: value);

        state.Alertness.Should().Be(value);
        state.Mood.Should().Be(value);
        state.Curiosity.Should().Be(value);
        state.Confidence.Should().Be(value);
    }

    // ── Apply ──

    [Fact]
    public void Apply_PositiveDelta_IncreasesEachDimension()
    {
        var initial = new EmotionState(30, 40, 20, 60);
        var delta = new EmotionDelta(Alertness: 10, Mood: 5, Curiosity: 15, Confidence: 20);

        var result = initial.Apply(delta);

        result.Alertness.Should().Be(40);
        result.Mood.Should().Be(45);
        result.Curiosity.Should().Be(35);
        result.Confidence.Should().Be(80);
    }

    [Fact]
    public void Apply_NegativeDelta_DecreasesEachDimension()
    {
        var initial = new EmotionState(70, 60, 80, 50);
        var delta = new EmotionDelta(Alertness: -10, Mood: -20, Curiosity: -30, Confidence: -5);

        var result = initial.Apply(delta);

        result.Alertness.Should().Be(60);
        result.Mood.Should().Be(40);
        result.Curiosity.Should().Be(50);
        result.Confidence.Should().Be(45);
    }

    [Fact]
    public void Apply_ZeroDelta_ReturnsSameValues()
    {
        var initial = new EmotionState(30, 70, 60, 45);
        var result = initial.Apply(EmotionDelta.Zero);

        result.Alertness.Should().Be(initial.Alertness);
        result.Mood.Should().Be(initial.Mood);
        result.Curiosity.Should().Be(initial.Curiosity);
        result.Confidence.Should().Be(initial.Confidence);
    }

    [Fact]
    public void Apply_DeltaThatExceedsMax_ClampsTo100()
    {
        var initial = new EmotionState(90, 90, 90, 90);
        var delta = new EmotionDelta(Alertness: 20, Mood: 100, Curiosity: 50, Confidence: 11);

        var result = initial.Apply(delta);

        result.Alertness.Should().Be(100);
        result.Mood.Should().Be(100);
        result.Curiosity.Should().Be(100);
        result.Confidence.Should().Be(100);
    }

    [Fact]
    public void Apply_DeltaThatGoesNegative_ClampsTo0()
    {
        var initial = new EmotionState(5, 10, 3, 8);
        var delta = new EmotionDelta(Alertness: -20, Mood: -50, Curiosity: -100, Confidence: -9);

        var result = initial.Apply(delta);

        result.Alertness.Should().Be(0);
        result.Mood.Should().Be(0);
        result.Curiosity.Should().Be(0);
        result.Confidence.Should().Be(0);
    }

    [Fact]
    public void Apply_IsImmutable_OriginalUnchanged()
    {
        var initial = new EmotionState(50, 50, 50, 50);
        _ = initial.Apply(new EmotionDelta(Alertness: 30, Mood: -30));

        // 原始实例不应改变
        initial.Alertness.Should().Be(50);
        initial.Mood.Should().Be(50);
    }

    [Fact]
    public void Apply_ChainedMultipleTimes_AccumulatesCorrectly()
    {
        var state = new EmotionState(50, 50, 50, 50);
        var delta = new EmotionDelta(Alertness: 5, Mood: -5, Curiosity: 3, Confidence: 0);

        // 连续应用 4 次
        for (int i = 0; i < 4; i++)
            state = state.Apply(delta);

        state.Alertness.Should().Be(70);
        state.Mood.Should().Be(30);
        state.Curiosity.Should().Be(62);
        state.Confidence.Should().Be(50);
    }

    // ── Clamp 静态方法 ──

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(int.MinValue, 0)]
    [InlineData(int.MaxValue, 100)]
    public void Clamp_ReturnsExpected(int input, int expected)
    {
        EmotionState.Clamp(input).Should().Be(expected);
    }

    // ── Record 等值性 ──

    [Fact]
    public void EqualityByValue_SameDimensions_AreEqual()
    {
        var a = new EmotionState(10, 20, 30, 40);
        var b = new EmotionState(10, 20, 30, 40);

        a.Should().Be(b);
    }

    [Fact]
    public void EqualityByValue_DifferentDimensions_AreNotEqual()
    {
        var a = new EmotionState(10, 20, 30, 40);
        var b = new EmotionState(10, 20, 30, 41);

        a.Should().NotBe(b);
    }

    // ── ToString ──

    [Fact]
    public void ToString_ContainsAllDimensionValues()
    {
        var state = new EmotionState(10, 20, 30, 40);
        var str = state.ToString();

        str.Should().Contain("10");
        str.Should().Contain("20");
        str.Should().Contain("30");
        str.Should().Contain("40");
    }
}
