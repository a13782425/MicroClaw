using FluentAssertions;
using MicroClaw.Agent.Middleware;
using Microsoft.Extensions.Logging.Abstractions;

namespace MicroClaw.Tests.Agents;

public sealed class ToolExecutionTimingMiddlewareTests
{
    // ── CreateCapture ─────────────────────────────────────────────────────

    [Fact]
    public void CreateCapture_InitialStateIsEmpty()
    {
        var capture = ToolExecutionTimingMiddleware.CreateCapture();

        capture.ToolCount.Should().Be(0);
        capture.GetSnapshot().Should().BeEmpty();
    }

    // ── TimingCapture.Record ──────────────────────────────────────────────

    [Fact]
    public void Record_SingleSuccessfulCall_CapturesBaseStats()
    {
        var capture = ToolExecutionTimingMiddleware.CreateCapture();

        capture.Record("my_tool", 150, true);

        var snapshot = capture.GetSnapshot();
        snapshot.Should().ContainKey("my_tool");
        var stats = snapshot["my_tool"];
        stats.CallCount.Should().Be(1);
        stats.ErrorCount.Should().Be(0);
        stats.TotalElapsedMs.Should().Be(150);
        stats.MaxElapsedMs.Should().Be(150);
        stats.AverageElapsedMs.Should().Be(150);
    }

    [Fact]
    public void Record_SingleFailedCall_CountsErrorCorrectly()
    {
        var capture = ToolExecutionTimingMiddleware.CreateCapture();

        capture.Record("err_tool", 50, false);

        var stats = capture.GetSnapshot()["err_tool"];
        stats.CallCount.Should().Be(1);
        stats.ErrorCount.Should().Be(1);
    }

    [Fact]
    public void Record_MultipleCalls_AccumulatesAllStats()
    {
        var capture = ToolExecutionTimingMiddleware.CreateCapture();

        capture.Record("tool_a", 100, true);
        capture.Record("tool_a", 300, true);
        capture.Record("tool_a", 200, false);

        var stats = capture.GetSnapshot()["tool_a"];
        stats.CallCount.Should().Be(3);
        stats.ErrorCount.Should().Be(1);
        stats.TotalElapsedMs.Should().Be(600);
        stats.MaxElapsedMs.Should().Be(300);
        stats.AverageElapsedMs.Should().BeApproximately(200, 0.01);
    }

    [Fact]
    public void Record_MultipleDistinctTools_TracksEachSeparately()
    {
        var capture = ToolExecutionTimingMiddleware.CreateCapture();

        capture.Record("tool_a", 100, true);
        capture.Record("tool_b", 200, true);

        capture.ToolCount.Should().Be(2);
        capture.GetSnapshot().Should().ContainKeys("tool_a", "tool_b");
    }

    [Fact]
    public void Record_MaxElapsedMs_TracksPeakValue()
    {
        var capture = ToolExecutionTimingMiddleware.CreateCapture();

        capture.Record("search", 50, true);
        capture.Record("search", 500, true);
        capture.Record("search", 100, true);

        capture.GetSnapshot()["search"].MaxElapsedMs.Should().Be(500);
    }

    [Fact]
    public void Record_AverageElapsedMs_IsCorrectFraction()
    {
        var capture = ToolExecutionTimingMiddleware.CreateCapture();

        capture.Record("tool", 100, true);
        capture.Record("tool", 200, true);

        capture.GetSnapshot()["tool"].AverageElapsedMs.Should().BeApproximately(150, 0.01);
    }

    // ── ToolStats record ──────────────────────────────────────────────────

    [Fact]
    public void ToolStats_AverageElapsedMs_ZeroWhenNoCallCount()
    {
        var stats = new ToolStats(0, 0, 0, 0);

        stats.AverageElapsedMs.Should().Be(0);
    }

    // ── Static Record facade ──────────────────────────────────────────────

    [Fact]
    public void StaticRecord_ForwardsToCapture()
    {
        var capture = ToolExecutionTimingMiddleware.CreateCapture();

        ToolExecutionTimingMiddleware.Record(
            capture, "search_tool", 300, true,
            NullLogger.Instance, slowThresholdMs: 5_000);

        capture.GetSnapshot().Should().ContainKey("search_tool");
        capture.GetSnapshot()["search_tool"].CallCount.Should().Be(1);
    }

    [Fact]
    public void StaticRecord_SlowToolExceedingThreshold_StillRecords()
    {
        var capture = ToolExecutionTimingMiddleware.CreateCapture();

        // slowThresholdMs が小さいため警告ログが記録されるはずだが、統計は変わらない
        ToolExecutionTimingMiddleware.Record(
            capture, "slow_tool", 10_000, true,
            NullLogger.Instance, slowThresholdMs: 100);

        capture.GetSnapshot()["slow_tool"].TotalElapsedMs.Should().Be(10_000);
    }
}
