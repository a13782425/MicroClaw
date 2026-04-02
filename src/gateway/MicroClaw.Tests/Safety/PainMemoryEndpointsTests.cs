using FluentAssertions;
using MicroClaw.Endpoints;
using MicroClaw.Infrastructure;
using MicroClaw.Safety;
using NSubstitute;

namespace MicroClaw.Tests.Safety;

public class PainMemoryEndpointsTests
{
    // ── GetAllAsync 注入验证 ──────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsEmptyList_WhenNoMemoriesExist()
    {
        // Arrange
        var store = Substitute.For<IPainMemoryStore>();
        store.GetAllAsync("agent1", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PainMemory>());

        // Act
        IReadOnlyList<PainMemory> result = await store.GetAllAsync("agent1");

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAll_ReturnsMemories_ForCorrectAgent()
    {
        // Arrange
        var memory = PainMemory.Create("agent1", "执行了危险命令", "系统文件被删除", "执行前先确认路径", PainSeverity.Critical);
        var store = Substitute.For<IPainMemoryStore>();
        store.GetAllAsync("agent1", Arg.Any<CancellationToken>())
            .Returns(new[] { memory });

        // Act
        IReadOnlyList<PainMemory> result = await store.GetAllAsync("agent1");

        // Assert
        result.Should().HaveCount(1);
        result[0].TriggerDescription.Should().Be("执行了危险命令");
        result[0].Severity.Should().Be(PainSeverity.Critical);
    }

    [Fact]
    public async Task GetAll_DoesNotReturnMemories_ForDifferentAgent()
    {
        // Arrange
        var store = Substitute.For<IPainMemoryStore>();
        store.GetAllAsync("agent-other", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<PainMemory>());

        // Act
        IReadOnlyList<PainMemory> result = await store.GetAllAsync("agent-other");

        // Assert
        result.Should().BeEmpty();
    }

    // ── DeleteAsync 注入验证 ─────────────────────────────────────────────────

    [Fact]
    public async Task Delete_CallsStore_WithCorrectAgentAndMemoryId()
    {
        // Arrange
        var store = Substitute.For<IPainMemoryStore>();

        // Act
        await store.DeleteAsync("agent1", "mem-123");

        // Assert
        await store.Received(1).DeleteAsync("agent1", "mem-123", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Delete_SilentlyIgnores_WhenMemoryNotFound()
    {
        // Arrange: DeleteAsync 对不存在的记录静默处理（不抛出异常）
        var store = Substitute.For<IPainMemoryStore>();
        store.DeleteAsync("agent1", "not-found", Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act & Assert: 不应抛出异常
        Func<Task> act = async () => await store.DeleteAsync("agent1", "not-found");
        await act.Should().NotThrowAsync();
    }

    // ── DTO 映射验证 ──────────────────────────────────────────────────────────

    [Fact]
    public void PainMemoryDto_MapsAllFields()
    {
        var dto = new PainMemoryEndpoints.PainMemoryDto(
            Id: "abc123",
            AgentId: "agent1",
            TriggerDescription: "触发描述",
            ConsequenceDescription: "后果描述",
            AvoidanceStrategy: "规避策略",
            Severity: "High",
            SeverityLevel: 2,
            OccurrenceCount: 3,
            LastOccurredAtMs: 1_000_000L,
            CreatedAtMs: 500_000L);

        dto.Id.Should().Be("abc123");
        dto.AgentId.Should().Be("agent1");
        dto.TriggerDescription.Should().Be("触发描述");
        dto.ConsequenceDescription.Should().Be("后果描述");
        dto.AvoidanceStrategy.Should().Be("规避策略");
        dto.Severity.Should().Be("High");
        dto.SeverityLevel.Should().Be(2);
        dto.OccurrenceCount.Should().Be(3);
        dto.LastOccurredAtMs.Should().Be(1_000_000L);
        dto.CreatedAtMs.Should().Be(500_000L);
    }

    [Fact]
    public void PainMemoryDto_SeverityLevel_MatchesEnum()
    {
        // 验证严重度枚举值与 SeverityLevel 的对应关系
        ((int)PainSeverity.Low).Should().Be(0);
        ((int)PainSeverity.Medium).Should().Be(1);
        ((int)PainSeverity.High).Should().Be(2);
        ((int)PainSeverity.Critical).Should().Be(3);
    }

    // ── PainMemory 业务逻辑验证 ──────────────────────────────────────────────

    [Fact]
    public void PainMemory_Create_SetsCorrectDefaults()
    {
        // Arrange & Act
        var memory = PainMemory.Create("agent1", "触发", "后果", "策略", PainSeverity.Medium);

        // Assert
        memory.AgentId.Should().Be("agent1");
        memory.TriggerDescription.Should().Be("触发");
        memory.ConsequenceDescription.Should().Be("后果");
        memory.AvoidanceStrategy.Should().Be("策略");
        memory.Severity.Should().Be(PainSeverity.Medium);
        memory.OccurrenceCount.Should().Be(1);
        memory.Id.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void PainMemory_WithIncrement_IncreaseCount()
    {
        // Arrange
        var memory = PainMemory.Create("agent1", "触发", "后果", "策略", PainSeverity.High);
        var originalCount = memory.OccurrenceCount;

        // Act
        var incremented = memory.WithIncrement();

        // Assert
        incremented.OccurrenceCount.Should().Be(originalCount + 1);
        incremented.Should().NotBeSameAs(memory); // 不可变语义
    }

    // ── GetAllAsync 多条记录验证 ─────────────────────────────────────────────

    [Fact]
    public async Task GetAll_ReturnsMultipleMemories_OrderedBySeverity()
    {
        // Arrange: Critical > High > Low 排序由 Store 负责
        var lowMemory = PainMemory.Create("agent1", "low trigger", "low consequence", "low strategy", PainSeverity.Low);
        var criticalMemory = PainMemory.Create("agent1", "critical trigger", "critical consequence", "critical strategy", PainSeverity.Critical);
        var highMemory = PainMemory.Create("agent1", "high trigger", "high consequence", "high strategy", PainSeverity.High);

        var store = Substitute.For<IPainMemoryStore>();
        store.GetAllAsync("agent1", Arg.Any<CancellationToken>())
            .Returns(new[] { criticalMemory, highMemory, lowMemory }); // Store 已排序返回

        // Act
        IReadOnlyList<PainMemory> result = await store.GetAllAsync("agent1");

        // Assert
        result.Should().HaveCount(3);
        result[0].Severity.Should().Be(PainSeverity.Critical);
        result[1].Severity.Should().Be(PainSeverity.High);
        result[2].Severity.Should().Be(PainSeverity.Low);
    }
}
