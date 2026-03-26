using FluentAssertions;
using MicroClaw.Agent.Middleware;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicroClaw.Tests.Agents;

public sealed class MaxIterationsMiddlewareTests
{
    // ── Clamp ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(50, 50)]
    [InlineData(51, 50)]
    [InlineData(100, 50)]
    public void Clamp_ReturnsValueWithinRange(int input, int expected)
    {
        MaxIterationsMiddleware.Clamp(input).Should().Be(expected);
    }

    [Fact]
    public void Clamp_WithMinBound_ReturnsMin()
    {
        MaxIterationsMiddleware.Clamp(int.MinValue).Should().Be(MaxIterationsMiddleware.MinIterations);
    }

    [Fact]
    public void Clamp_WithMaxBound_ReturnsMax()
    {
        MaxIterationsMiddleware.Clamp(int.MaxValue).Should().Be(MaxIterationsMiddleware.MaxIterations);
    }

    // ── IterationCounter — 初始状态 ───────────────────────────────────────

    [Fact]
    public void CreateCounter_InitialCurrentIterationIsZero()
    {
        var counter = MaxIterationsMiddleware.CreateCounter(10, NullLogger.Instance);

        counter.CurrentIteration.Should().Be(0);
        counter.IsAtLimit.Should().BeFalse();
    }

    [Fact]
    public void CreateCounter_ClampsMaxIterationsToAllowedRange()
    {
        var counter = MaxIterationsMiddleware.CreateCounter(100, NullLogger.Instance);

        counter.MaxIterations.Should().Be(MaxIterationsMiddleware.MaxIterations);
    }

    [Fact]
    public void CreateCounter_BelowMinimum_ClampsToMin()
    {
        var counter = MaxIterationsMiddleware.CreateCounter(0, NullLogger.Instance);

        counter.MaxIterations.Should().Be(MaxIterationsMiddleware.MinIterations);
    }

    // ── IterationCounter — Increment ───────────────────────────────────────

    [Fact]
    public void Increment_ReturnsOneBasedCount()
    {
        var counter = MaxIterationsMiddleware.CreateCounter(10, NullLogger.Instance);

        counter.Increment().Should().Be(1);
        counter.Increment().Should().Be(2);
        counter.CurrentIteration.Should().Be(2);
    }

    [Fact]
    public void Increment_SequentialCalls_AccumulatesCorrectly()
    {
        var counter = MaxIterationsMiddleware.CreateCounter(5, NullLogger.Instance);

        for (int i = 1; i <= 5; i++)
            counter.Increment().Should().Be(i);

        counter.CurrentIteration.Should().Be(5);
    }

    // ── IterationCounter — IsAtLimit ──────────────────────────────────────

    [Fact]
    public void IsAtLimit_FalseBeforeReachingMax()
    {
        var counter = MaxIterationsMiddleware.CreateCounter(3, NullLogger.Instance);

        counter.Increment();
        counter.Increment();

        counter.IsAtLimit.Should().BeFalse();
    }

    [Fact]
    public void IsAtLimit_TrueWhenCountReachesMax()
    {
        var counter = MaxIterationsMiddleware.CreateCounter(3, NullLogger.Instance);

        counter.Increment();
        counter.Increment();
        counter.Increment();

        counter.IsAtLimit.Should().BeTrue();
    }

    // ── IterationCounter — Reset ───────────────────────────────────────────

    [Fact]
    public void Reset_SetsCountBackToZero()
    {
        var counter = MaxIterationsMiddleware.CreateCounter(5, NullLogger.Instance);

        counter.Increment();
        counter.Increment();
        counter.Reset();

        counter.CurrentIteration.Should().Be(0);
        counter.IsAtLimit.Should().BeFalse();
    }

    [Fact]
    public void Reset_AllowsIncrementAfterReset()
    {
        var counter = MaxIterationsMiddleware.CreateCounter(2, NullLogger.Instance);

        counter.Increment();
        counter.Increment();
        counter.IsAtLimit.Should().BeTrue();

        counter.Reset();
        counter.Increment();
        counter.IsAtLimit.Should().BeFalse();
    }
}
