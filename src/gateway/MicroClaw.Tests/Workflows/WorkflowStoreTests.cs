using FluentAssertions;
using MicroClaw.Agent.Workflows;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Workflows;

public sealed class WorkflowStoreTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly WorkflowStore _store;

    public WorkflowStoreTests()
    {
        _store = new WorkflowStore(_tempDir.Path);
    }

    public void Dispose() => _tempDir.Dispose();

    private static WorkflowConfig CreateSampleWorkflow(
        string name = "Test Workflow",
        bool isEnabled = true,
        IReadOnlyList<WorkflowNodeConfig>? nodes = null,
        IReadOnlyList<WorkflowEdgeConfig>? edges = null) =>
        new(
            Id: string.Empty,
            Name: name,
            Description: "A test workflow.",
            IsEnabled: isEnabled,
            Nodes: nodes ?? [],
            Edges: edges ?? [],
            EntryNodeId: null,
            DefaultProviderId: null,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow);

    // ── 基本 CRUD ─────────────────────────────────────────────────────────

    [Fact]
    public void All_WhenEmpty_ReturnsEmptyList()
    {
        _store.All.Should().BeEmpty();
    }

    [Fact]
    public void Add_CreatesWorkflowWithGeneratedId()
    {
        var result = _store.Add(CreateSampleWorkflow());

        result.Id.Should().NotBeNullOrWhiteSpace();
        result.Name.Should().Be("Test Workflow");
        result.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Add_ThenAll_ContainsWorkflow()
    {
        var added = _store.Add(CreateSampleWorkflow());

        _store.All.Should().ContainSingle()
            .Which.Id.Should().Be(added.Id);
    }

    [Fact]
    public void GetById_ExistingWorkflow_ReturnsIt()
    {
        var added = _store.Add(CreateSampleWorkflow());

        var result = _store.GetById(added.Id);

        result.Should().NotBeNull();
        result!.Id.Should().Be(added.Id);
        result.Name.Should().Be("Test Workflow");
    }

    [Fact]
    public void GetById_NonExistent_ReturnsNull()
    {
        _store.GetById("non-existent").Should().BeNull();
    }

    [Fact]
    public void Delete_ExistingWorkflow_ReturnsTrueAndRemoves()
    {
        var added = _store.Add(CreateSampleWorkflow());

        bool deleted = _store.Delete(added.Id);

        deleted.Should().BeTrue();
        _store.GetById(added.Id).Should().BeNull();
        _store.All.Should().BeEmpty();
    }

    [Fact]
    public void Delete_NonExistent_ReturnsFalse()
    {
        _store.Delete("non-existent").Should().BeFalse();
    }

    [Fact]
    public void Update_ExistingWorkflow_AppliesChanges()
    {
        var added = _store.Add(CreateSampleWorkflow("Original"));

        var updated = added with { Name = "Updated Name", IsEnabled = false };
        var result = _store.Update(added.Id, updated);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated Name");
        result.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Update_NonExistent_ReturnsNull()
    {
        var config = CreateSampleWorkflow() with { Id = "non-existent" };
        _store.Update("non-existent", config).Should().BeNull();
    }

    // ── 节点和边持久化 ────────────────────────────────────────────────────

    [Fact]
    public void Add_WithNodes_PersistsNodesCorrectly()
    {
        var nodes = (IReadOnlyList<WorkflowNodeConfig>)
        [
            new WorkflowNodeConfig("start", "开始", WorkflowNodeType.Start, null, null, null, null, null),
            new WorkflowNodeConfig("agent1", "执行 Agent", WorkflowNodeType.Agent, "agent-id-1", null, null, null,
                new WorkflowPosition(100, 200)),
            new WorkflowNodeConfig("end", "结束", WorkflowNodeType.End, null, null, null, null, null)
        ];
        var edges = (IReadOnlyList<WorkflowEdgeConfig>)
        [
            new WorkflowEdgeConfig("start", "agent1", null, null),
            new WorkflowEdgeConfig("agent1", "end", null, "完成")
        ];

        var added = _store.Add(CreateSampleWorkflow(nodes: nodes, edges: edges));
        var retrieved = _store.GetById(added.Id)!;

        retrieved.Nodes.Should().HaveCount(3);
        retrieved.Nodes[1].NodeId.Should().Be("agent1");
        retrieved.Nodes[1].AgentId.Should().Be("agent-id-1");
        retrieved.Nodes[1].Position!.X.Should().Be(100);

        retrieved.Edges.Should().HaveCount(2);
        retrieved.Edges[1].Label.Should().Be("完成");
    }

    [Fact]
    public void Add_WithEntryNodeId_PersistsEntryNode()
    {
        var workflow = CreateSampleWorkflow() with { EntryNodeId = "start-node" };
        var added = _store.Add(workflow);
        var retrieved = _store.GetById(added.Id)!;

        retrieved.EntryNodeId.Should().Be("start-node");
    }

    [Fact]
    public void Add_MultipleWorkflows_AllListedInAll()
    {
        _store.Add(CreateSampleWorkflow("W1"));
        _store.Add(CreateSampleWorkflow("W2"));
        _store.Add(CreateSampleWorkflow("W3"));

        _store.All.Should().HaveCount(3);
    }
}
