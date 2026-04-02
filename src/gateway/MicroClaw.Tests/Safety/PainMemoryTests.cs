using FluentAssertions;
using MicroClaw.Infrastructure;
using MicroClaw.Safety;

namespace MicroClaw.Tests.Safety;

public class PainMemoryTests
{
    // ── PainMemory.Create — 参数校验 ──────────────────────────────────────────

    [Fact]
    public void Create_NullAgentId_Throws()
    {
        var act = () => PainMemory.Create(null!, "trigger", "consequence", "strategy", PainSeverity.Low);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_EmptyAgentId_Throws()
    {
        var act = () => PainMemory.Create("", "trigger", "consequence", "strategy", PainSeverity.Low);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WhitespaceAgentId_Throws()
    {
        var act = () => PainMemory.Create("   ", "trigger", "consequence", "strategy", PainSeverity.Low);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_NullTrigger_Throws()
    {
        var act = () => PainMemory.Create("agent1", null!, "consequence", "strategy", PainSeverity.Low);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_EmptyTrigger_Throws()
    {
        var act = () => PainMemory.Create("agent1", "", "consequence", "strategy", PainSeverity.Low);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_NullConsequence_Throws()
    {
        var act = () => PainMemory.Create("agent1", "trigger", null!, "strategy", PainSeverity.Low);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_NullAvoidanceStrategy_Throws()
    {
        var act = () => PainMemory.Create("agent1", "trigger", "consequence", null!, PainSeverity.Low);
        act.Should().Throw<ArgumentException>();
    }

    // ── PainMemory.Create — 正常创建 ──────────────────────────────────────────

    [Fact]
    public void Create_ValidParams_SetsAllFields()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var memory = PainMemory.Create(
            agentId: "agent1",
            triggerDescription: "调用了危险 shell 命令",
            consequenceDescription: "删除了重要文件",
            avoidanceStrategy: "执行 rm 前先确认路径",
            severity: PainSeverity.Critical);

        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        memory.AgentId.Should().Be("agent1");
        memory.TriggerDescription.Should().Be("调用了危险 shell 命令");
        memory.ConsequenceDescription.Should().Be("删除了重要文件");
        memory.AvoidanceStrategy.Should().Be("执行 rm 前先确认路径");
        memory.Severity.Should().Be(PainSeverity.Critical);
        memory.OccurrenceCount.Should().Be(1);
        memory.Id.Should().NotBeNullOrWhiteSpace().And.HaveLength(32);
        memory.CreatedAtMs.Should().BeInRange(before, after);
        memory.LastOccurredAtMs.Should().BeInRange(before, after);
    }

    [Fact]
    public void Create_TwoCallsHaveDifferentIds()
    {
        var m1 = PainMemory.Create("a", "t", "c", "s", PainSeverity.Low);
        var m2 = PainMemory.Create("a", "t", "c", "s", PainSeverity.Low);

        m1.Id.Should().NotBe(m2.Id);
    }

    [Theory]
    [InlineData(PainSeverity.Low)]
    [InlineData(PainSeverity.Medium)]
    [InlineData(PainSeverity.High)]
    [InlineData(PainSeverity.Critical)]
    public void Create_AllSeverityLevels_AreValid(PainSeverity severity)
    {
        var memory = PainMemory.Create("agent1", "trigger", "consequence", "strategy", severity);
        memory.Severity.Should().Be(severity);
    }

    // ── PainMemory.WithIncrement — 不可变语义 ─────────────────────────────────

    [Fact]
    public void WithIncrement_IncreasesOccurrenceCountByOne()
    {
        var original = PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.Medium);
        original.OccurrenceCount.Should().Be(1);

        var updated = original.WithIncrement();
        updated.OccurrenceCount.Should().Be(2);
    }

    [Fact]
    public void WithIncrement_DoesNotMutateOriginal()
    {
        var original = PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.Low);
        _ = original.WithIncrement();

        original.OccurrenceCount.Should().Be(1);
    }

    [Fact]
    public void WithIncrement_PreservesAllOtherFields()
    {
        var original = PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.High);

        var updated = original.WithIncrement();

        updated.Id.Should().Be(original.Id);
        updated.AgentId.Should().Be(original.AgentId);
        updated.TriggerDescription.Should().Be(original.TriggerDescription);
        updated.ConsequenceDescription.Should().Be(original.ConsequenceDescription);
        updated.AvoidanceStrategy.Should().Be(original.AvoidanceStrategy);
        updated.Severity.Should().Be(original.Severity);
        updated.CreatedAtMs.Should().Be(original.CreatedAtMs);
    }

    [Fact]
    public void WithIncrement_UpdatesLastOccurredAtMs()
    {
        var original = PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.Low);
        var before = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var updated = original.WithIncrement();

        var after = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        updated.LastOccurredAtMs.Should().BeInRange(before, after);
    }

    [Fact]
    public void WithIncrement_ChainedThreeTimes_OccurrenceCountIsCorrect()
    {
        var memory = PainMemory.Create("agent1", "trigger", "consequence", "strategy", PainSeverity.Low);

        var result = memory.WithIncrement().WithIncrement().WithIncrement();

        result.OccurrenceCount.Should().Be(4);
    }

    // ── PainSeverity 枚举值验证 ───────────────────────────────────────────────

    [Fact]
    public void PainSeverity_HasCorrectOrdinalValues()
    {
        ((int)PainSeverity.Low).Should().Be(0);
        ((int)PainSeverity.Medium).Should().Be(1);
        ((int)PainSeverity.High).Should().Be(2);
        ((int)PainSeverity.Critical).Should().Be(3);
    }

    [Fact]
    public void PainSeverity_Critical_IsGreaterThanHigh()
    {
        (PainSeverity.Critical > PainSeverity.High).Should().BeTrue();
        (PainSeverity.High > PainSeverity.Medium).Should().BeTrue();
        (PainSeverity.Medium > PainSeverity.Low).Should().BeTrue();
    }
}
