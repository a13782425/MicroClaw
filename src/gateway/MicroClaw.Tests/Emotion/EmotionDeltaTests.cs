using FluentAssertions;
using MicroClaw.Emotion;

namespace MicroClaw.Tests.Emotion;

public class EmotionDeltaTests
{
    // ── 零增减量 ──

    [Fact]
    public void Zero_AllDimensionsAreZero()
    {
        var delta = EmotionDelta.Zero;

        delta.Alertness.Should().Be(0);
        delta.Mood.Should().Be(0);
        delta.Curiosity.Should().Be(0);
        delta.Confidence.Should().Be(0);
    }

    // ── 默认参数构造 ──

    [Fact]
    public void DefaultCtor_AllDimensionsAreZero()
    {
        var delta = new EmotionDelta();

        delta.Alertness.Should().Be(0);
        delta.Mood.Should().Be(0);
        delta.Curiosity.Should().Be(0);
        delta.Confidence.Should().Be(0);
    }

    [Fact]
    public void PartialCtor_UnspecifiedDimensionsAreZero()
    {
        var delta = new EmotionDelta(Alertness: 10, Confidence: -5);

        delta.Alertness.Should().Be(10);
        delta.Mood.Should().Be(0);
        delta.Curiosity.Should().Be(0);
        delta.Confidence.Should().Be(-5);
    }

    // ── Merge ──

    [Fact]
    public void Merge_SumsEachDimension()
    {
        var a = new EmotionDelta(Alertness: 10, Mood: -5, Curiosity: 3, Confidence: 0);
        var b = new EmotionDelta(Alertness: -2, Mood: 15, Curiosity: -1, Confidence: 7);

        var merged = a.Merge(b);

        merged.Alertness.Should().Be(8);
        merged.Mood.Should().Be(10);
        merged.Curiosity.Should().Be(2);
        merged.Confidence.Should().Be(7);
    }

    [Fact]
    public void Merge_WithZero_ReturnsOriginalValues()
    {
        var delta = new EmotionDelta(Alertness: 5, Mood: -3, Curiosity: 10, Confidence: -8);
        var merged = delta.Merge(EmotionDelta.Zero);

        merged.Should().Be(delta);
    }

    // ── Scale ──

    [Fact]
    public void Scale_ByOne_ReturnsEquivalentDelta()
    {
        var delta = new EmotionDelta(Alertness: 10, Mood: -5, Curiosity: 3, Confidence: 8);
        var scaled = delta.Scale(1.0);

        scaled.Should().Be(delta);
    }

    [Fact]
    public void Scale_ByHalf_HalvesDimensions()
    {
        var delta = new EmotionDelta(Alertness: 10, Mood: -10, Curiosity: 6, Confidence: 4);
        var scaled = delta.Scale(0.5);

        scaled.Alertness.Should().Be(5);
        scaled.Mood.Should().Be(-5);
        scaled.Curiosity.Should().Be(3);
        scaled.Confidence.Should().Be(2);
    }

    [Fact]
    public void Scale_ByZero_ReturnsZeroDelta()
    {
        var delta = new EmotionDelta(Alertness: 50, Mood: -30, Curiosity: 20, Confidence: 10);
        var scaled = delta.Scale(0.0);

        scaled.Should().Be(EmotionDelta.Zero);
    }

    [Fact]
    public void Scale_ByTwo_DoublesDimensions()
    {
        var delta = new EmotionDelta(Alertness: 5, Mood: -3, Curiosity: 4, Confidence: 7);
        var scaled = delta.Scale(2.0);

        scaled.Alertness.Should().Be(10);
        scaled.Mood.Should().Be(-6);
        scaled.Curiosity.Should().Be(8);
        scaled.Confidence.Should().Be(14);
    }

    // ── Record 等值性 ──

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = new EmotionDelta(1, 2, 3, 4);
        var b = new EmotionDelta(1, 2, 3, 4);
        a.Should().Be(b);
    }

    [Fact]
    public void Equality_DifferentValues_NotEqual()
    {
        var a = new EmotionDelta(1, 2, 3, 4);
        var b = new EmotionDelta(1, 2, 3, 5);
        a.Should().NotBe(b);
    }
}
