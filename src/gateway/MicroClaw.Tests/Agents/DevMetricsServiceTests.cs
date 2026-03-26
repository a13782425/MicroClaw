using FluentAssertions;
using MicroClaw.Agent.Dev;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// 验证 DevMetricsService 的指标聚合逻辑。
/// 覆盖：工具记录、Agent 运行记录、快照快照字段、并发安全性、最近记录上限（100 条）。
/// </summary>
public sealed class DevMetricsServiceTests
{
    private static DevMetricsService CreateSut() => new();

    // ── 初始状态 ────────────────────────────────────────────────────────────

    [Fact]
    public void GetSnapshot_WhenNew_ReturnsEmptyMetrics()
    {
        var sut = CreateSut();

        DevMetricsSnapshot snap = sut.GetSnapshot();

        snap.TotalAgentRuns.Should().Be(0);
        snap.FailedAgentRuns.Should().Be(0);
        snap.ToolStats.Should().BeEmpty();
        snap.RecentRuns.Should().BeEmpty();
        snap.StartedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ── 工具执行记录 ─────────────────────────────────────────────────────────

    [Fact]
    public void RecordToolExecution_SingleSuccess_CorrectStats()
    {
        var sut = CreateSut();

        sut.RecordToolExecution("fetch_url", 120, success: true);

        DevMetricsSnapshot snap = sut.GetSnapshot();
        snap.ToolStats.Should().ContainKey("fetch_url");
        ToolStatsDto dto = snap.ToolStats["fetch_url"];
        dto.CallCount.Should().Be(1);
        dto.ErrorCount.Should().Be(0);
        dto.TotalElapsedMs.Should().Be(120);
        dto.MaxElapsedMs.Should().Be(120);
        dto.AverageElapsedMs.Should().BeApproximately(120.0, 0.01);
    }

    [Fact]
    public void RecordToolExecution_SingleFailure_IncreasesErrorCount()
    {
        var sut = CreateSut();

        sut.RecordToolExecution("shell", 50, success: false);

        ToolStatsDto dto = sut.GetSnapshot().ToolStats["shell"];
        dto.ErrorCount.Should().Be(1);
        dto.CallCount.Should().Be(1);
    }

    [Fact]
    public void RecordToolExecution_MultipleCalls_AccumulatesStats()
    {
        var sut = CreateSut();

        sut.RecordToolExecution("tool_a", 100, success: true);
        sut.RecordToolExecution("tool_a", 200, success: true);
        sut.RecordToolExecution("tool_a", 50,  success: false);

        ToolStatsDto dto = sut.GetSnapshot().ToolStats["tool_a"];
        dto.CallCount.Should().Be(3);
        dto.ErrorCount.Should().Be(1);
        dto.TotalElapsedMs.Should().Be(350);
        dto.MaxElapsedMs.Should().Be(200);
        dto.AverageElapsedMs.Should().BeApproximately(350.0 / 3, 0.01);
    }

    [Fact]
    public void RecordToolExecution_MultipleTools_TrackedSeparately()
    {
        var sut = CreateSut();

        sut.RecordToolExecution("tool_x", 10, success: true);
        sut.RecordToolExecution("tool_y", 20, success: true);

        var snap = sut.GetSnapshot().ToolStats;
        snap.Should().ContainKey("tool_x");
        snap.Should().ContainKey("tool_y");
        snap["tool_x"].CallCount.Should().Be(1);
        snap["tool_y"].CallCount.Should().Be(1);
    }

    // ── Agent 运行记录 ───────────────────────────────────────────────────────

    [Fact]
    public void RecordAgentRun_Success_IncrementsTotalOnly()
    {
        var sut = CreateSut();

        sut.RecordAgentRun("agent-1", success: true, durationMs: 1000);

        var snap = sut.GetSnapshot();
        snap.TotalAgentRuns.Should().Be(1);
        snap.FailedAgentRuns.Should().Be(0);
    }

    [Fact]
    public void RecordAgentRun_Failure_IncrementsBothCounters()
    {
        var sut = CreateSut();

        sut.RecordAgentRun("agent-1", success: false, durationMs: 500);

        var snap = sut.GetSnapshot();
        snap.TotalAgentRuns.Should().Be(1);
        snap.FailedAgentRuns.Should().Be(1);
    }

    [Fact]
    public void RecordAgentRun_MultipleRuns_CountsAccurate()
    {
        var sut = CreateSut();

        sut.RecordAgentRun("a1", success: true,  durationMs: 100);
        sut.RecordAgentRun("a2", success: true,  durationMs: 200);
        sut.RecordAgentRun("a3", success: false, durationMs: 300);

        var snap = sut.GetSnapshot();
        snap.TotalAgentRuns.Should().Be(3);
        snap.FailedAgentRuns.Should().Be(1);
    }

    [Fact]
    public void RecordAgentRun_AppearsInRecentRuns()
    {
        var sut = CreateSut();

        sut.RecordAgentRun("agent-x", success: true, durationMs: 42);

        IReadOnlyList<AgentRunRecord> runs = sut.GetSnapshot().RecentRuns;
        runs.Should().HaveCount(1);
        runs[0].AgentId.Should().Be("agent-x");
        runs[0].Success.Should().BeTrue();
        runs[0].DurationMs.Should().Be(42);
        runs[0].ExecutedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RecordAgentRun_Over100_KeepsOnly100()
    {
        var sut = CreateSut();

        for (int i = 0; i < 110; i++)
            sut.RecordAgentRun($"agent-{i}", success: true, durationMs: i);

        sut.GetSnapshot().RecentRuns.Should().HaveCountLessThan(101);
    }

    // ── 并发安全 ─────────────────────────────────────────────────────────────

    [Fact]
    public void RecordToolExecution_ConcurrentCalls_CountsCorrect()
    {
        var sut = CreateSut();
        const int threadCount = 10;
        const int callsPerThread = 100;

        Parallel.For(0, threadCount, _ =>
        {
            for (int i = 0; i < callsPerThread; i++)
                sut.RecordToolExecution("concurrent_tool", 1, success: true);
        });

        sut.GetSnapshot().ToolStats["concurrent_tool"].CallCount.Should().Be(threadCount * callsPerThread);
    }

    [Fact]
    public void RecordAgentRun_ConcurrentCalls_TotalRunsAccurate()
    {
        var sut = CreateSut();
        const int count = 200;

        Parallel.For(0, count, i =>
            sut.RecordAgentRun($"a{i}", success: i % 3 != 0, durationMs: i));

        var snap = sut.GetSnapshot();
        snap.TotalAgentRuns.Should().Be(count);
    }

    // ── 快照不可变性 ─────────────────────────────────────────────────────────

    [Fact]
    public void GetSnapshot_ReturnsIndependentCopy()
    {
        var sut = CreateSut();
        sut.RecordToolExecution("tool", 10, success: true);

        DevMetricsSnapshot snap1 = sut.GetSnapshot();
        sut.RecordToolExecution("tool", 20, success: true);
        DevMetricsSnapshot snap2 = sut.GetSnapshot();

        snap1.ToolStats["tool"].CallCount.Should().Be(1);
        snap2.ToolStats["tool"].CallCount.Should().Be(2);
    }
}
