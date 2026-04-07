using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// 测试 AgentStore 中 ExposeAsA2A 字段的持久化和更新。
/// </summary>
public sealed class A2AAgentStoreTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly AgentStore _store;

    public A2AAgentStoreTests()
    {
        TestConfigFixture.EnsureInitialized();
        _store = new AgentStore();
    }

    public void Dispose() => _tempDir.Dispose();

    private static AgentConfig CreateSampleConfig(
        string name = "Test Agent",
        bool exposeAsA2A = false) =>
        new(
            Id: string.Empty,
            Name: name,
            Description: "A test agent.",
            IsEnabled: true,
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow,
            ExposeAsA2A: exposeAsA2A);

    // ── ExposeAsA2A 默认值 ─────────────────────────────────────────────────────

    [Fact]
    public void Add_WithDefaultExposeAsA2A_IsFalse()
    {
        var result = _store.Add(CreateSampleConfig());

        result.ExposeAsA2A.Should().BeFalse();
    }

    [Fact]
    public void Add_WithExposeAsA2ATrue_PersistsAsTrue()
    {
        var result = _store.Add(CreateSampleConfig(exposeAsA2A: true));

        result.ExposeAsA2A.Should().BeTrue();
    }

    // ── 持久化往返（Store + Reload）─────────────────────────────────────────────

    [Fact]
    public void Add_ExposeAsA2ATrue_CanBeReloadedFromStore()
    {
        var added = _store.Add(CreateSampleConfig(exposeAsA2A: true));

        var loaded = _store.GetById(added.Id);

        loaded.Should().NotBeNull();
        loaded!.ExposeAsA2A.Should().BeTrue();
    }

    [Fact]
    public void Add_ExposeAsA2AFalse_CanBeReloadedFromStore()
    {
        var added = _store.Add(CreateSampleConfig(exposeAsA2A: false));

        var loaded = _store.GetById(added.Id);

        loaded.Should().NotBeNull();
        loaded!.ExposeAsA2A.Should().BeFalse();
    }

    [Fact]
    public void All_ReturnsCorrectExposeAsA2AForEachAgent()
    {
        _store.Add(CreateSampleConfig("agent-a", exposeAsA2A: false));
        _store.Add(CreateSampleConfig("agent-b", exposeAsA2A: true));

        var all = _store.All;

        all.Should().HaveCount(2);
        all.Single(a => a.Name == "agent-a").ExposeAsA2A.Should().BeFalse();
        all.Single(a => a.Name == "agent-b").ExposeAsA2A.Should().BeTrue();
    }

    // ── Update 数据更新 ──────────────────────────────────────────────────────

    [Fact]
    public void Update_EnablesExposeAsA2A_PersistsTrueValue()
    {
        var added = _store.Add(CreateSampleConfig(exposeAsA2A: false));

        var result = _store.Update(added.Id, added with { ExposeAsA2A = true });

        result.Should().NotBeNull();
        result!.ExposeAsA2A.Should().BeTrue();
        _store.GetById(added.Id)!.ExposeAsA2A.Should().BeTrue();
    }

    [Fact]
    public void Update_DisablesExposeAsA2A_PersistsFalseValue()
    {
        var added = _store.Add(CreateSampleConfig(exposeAsA2A: true));

        var result = _store.Update(added.Id, added with { ExposeAsA2A = false });

        result.Should().NotBeNull();
        result!.ExposeAsA2A.Should().BeFalse();
        _store.GetById(added.Id)!.ExposeAsA2A.Should().BeFalse();
    }

    [Fact]
    public void Update_UnchangedExposeAsA2A_PreservesValue()
    {
        var added = _store.Add(CreateSampleConfig(exposeAsA2A: true));

        // 更新其他字段，ExposeAsA2A 保持不变
        var result = _store.Update(added.Id, added with { Description = "Updated description" });

        result.Should().NotBeNull();
        result!.ExposeAsA2A.Should().BeTrue();
        result.Description.Should().Be("Updated description");
    }

    // ── EnsureMainAgent 默认不暴露 A2A ──────────────────────────────────────────

    [Fact]
    public void EnsureMainAgent_DefaultAgent_HasExposeAsA2AFalse()
    {
        var main = _store.EnsureMainAgent();

        main.ExposeAsA2A.Should().BeFalse();
    }
}
