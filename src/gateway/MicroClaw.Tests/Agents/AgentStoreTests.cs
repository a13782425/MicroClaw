using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Agents;

public sealed class AgentStoreTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly AgentStore _store;

    public AgentStoreTests()
    {
        TestConfigFixture.EnsureInitialized();
        _store = new AgentStore();
    }

    public void Dispose() => _tempDir.Dispose();

    private static AgentConfig CreateSampleConfig(
        string name = "Test Agent",
        bool isEnabled = true,
        bool isDefault = false) =>
        new(
            Id: string.Empty,
            Name: name,
            Description: "A test agent.",
            IsEnabled: isEnabled,
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow,
            IsDefault: isDefault);

    // --- CRUD 基础测试 ---

    [Fact]
    public void All_WhenEmpty_ReturnsEmptyList()
    {
        _store.All.Should().BeEmpty();
    }

    [Fact]
    public void Add_CreatesAgentWithGeneratedId()
    {
        var result = _store.Add(CreateSampleConfig());

        result.Id.Should().NotBeNullOrWhiteSpace();
        result.Name.Should().Be("Test Agent");
        result.IsEnabled.Should().BeTrue();
        result.IsDefault.Should().BeFalse();
    }

    [Fact]
    public void Add_ThenAll_ContainsAgent()
    {
        var added = _store.Add(CreateSampleConfig());

        _store.All.Should().ContainSingle()
            .Which.Id.Should().Be(added.Id);
    }

    [Fact]
    public void GetById_ExistingAgent_ReturnsIt()
    {
        var added = _store.Add(CreateSampleConfig());

        var result = _store.GetById(added.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(added.Id);
        result.Name.Should().Be("Test Agent");
    }

    [Fact]
    public void GetById_NonExistent_ReturnsNull()
    {
        _store.GetById("non-existent").Should().BeNull();
    }

    [Fact]
    public void Update_ExistingAgent_ReturnsUpdated()
    {
        var added = _store.Add(CreateSampleConfig(name: "Original"));
        var updated = added with { Name = "Updated", Description = "New description" };

        var result = _store.Update(added.Id, updated);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated");
        result.Description.Should().Be("New description");
    }

    [Fact]
    public void Update_NonExistent_ReturnsNull()
    {
        var result = _store.Update("non-existent", CreateSampleConfig());

        result.Should().BeNull();
    }

    [Fact]
    public void Delete_ExistingAgent_ReturnsTrue()
    {
        var added = _store.Add(CreateSampleConfig());

        var deleted = _store.Delete(added.Id);

        deleted.Should().BeTrue();
        _store.GetById(added.Id).Should().BeNull();
    }

    [Fact]
    public void Delete_NonExistent_ReturnsFalse()
    {
        _store.Delete("non-existent").Should().BeFalse();
    }

    // --- 默认代理相关测试 ---

    [Fact]
    public void GetDefault_WhenNoDefault_ReturnsNull()
    {
        _store.Add(CreateSampleConfig(name: "normal-agent"));

        _store.GetDefault().Should().BeNull();
    }

    [Fact]
    public void GetDefault_AfterEnsureMain_ReturnsMainAgent()
    {
        _store.EnsureMainAgent();

        var result = _store.GetDefault();

        result.Should().NotBeNull();
        result!.Name.Should().Be("main");
        result.IsDefault.Should().BeTrue();
    }

    [Fact]
    public void EnsureMainAgent_CalledTwice_CreatesOnlyOneDefault()
    {
        _store.EnsureMainAgent();
        _store.EnsureMainAgent();

        _store.All.Where(a => a.IsDefault).Should().ContainSingle();
    }

    [Fact]
    public void EnsureMainAgent_WhenDefaultExists_ReturnsExisting()
    {
        var first = _store.EnsureMainAgent();
        var second = _store.EnsureMainAgent();

        second.Id.Should().Be(first.Id);
    }

    // --- 保护逻辑测试 ---

    [Fact]
    public void Delete_DefaultAgent_ReturnsFalse()
    {
        _store.EnsureMainAgent();
        var main = _store.GetDefault()!;

        var result = _store.Delete(main.Id);

        result.Should().BeFalse();
        _store.GetById(main.Id).Should().NotBeNull();
    }

    [Fact]
    public void Update_DefaultAgent_KeepsOriginalName()
    {
        _store.EnsureMainAgent();
        var main = _store.GetDefault()!;

        _store.Update(main.Id, main with { Name = "hacked-name", Description = "new description" });

        var after = _store.GetById(main.Id)!;
        after.Name.Should().Be("main");
        after.Description.Should().Be("new description"); // 非名称字段可修改
    }

    // --- JSON 序列化往返测试 ---

    [Fact]
    public void DisabledSkillIds_RoundTrip_Preserved()
    {
        var skillIds = new[] { "skill-1", "skill-2", "skill-3" };
        var config = CreateSampleConfig() with { DisabledSkillIds = skillIds };

        var added = _store.Add(config);
        var retrieved = _store.GetById(added.Id)!;

        retrieved.DisabledSkillIds.Should().BeEquivalentTo(skillIds);
    }

    [Fact]
    public void DisabledMcpServerIds_RoundTrip_Preserved()
    {
        var mcpIds = new[] { "mcp-id-1", "mcp-id-2" };
        var config = CreateSampleConfig() with { DisabledMcpServerIds = mcpIds };

        var added = _store.Add(config);
        var retrieved = _store.GetById(added.Id)!;

        retrieved.DisabledMcpServerIds.Should().BeEquivalentTo(mcpIds);
    }
}
