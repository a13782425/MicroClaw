using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MicroClaw.Agent.Workflows;
using MicroClaw.Configuration;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Utils;

namespace MicroClaw.Tests;

public sealed class WorkflowStoreTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "microclaw-workflow-tests", Guid.NewGuid().ToString("N"));
    private readonly string _configDir;

    public WorkflowStoreTests()
    {
        _configDir = Path.Combine(_tempRoot, "config");
        Directory.CreateDirectory(_configDir);
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", _tempRoot);
        MicroClawConfig.Reset();
    }

    [Fact]
    public void All_WhenConfigured_ReturnsMappedWorkflows()
    {
        WorkflowNodeConfig node = new(
            NodeId: "start",
            Label: "Start",
            Type: WorkflowNodeType.Start,
            AgentId: null,
            FunctionName: null,
            ProviderId: null,
            Config: null,
            Position: new WorkflowPosition(10, 20));

        WorkflowEdgeConfig edge = new("start", "end", null, "next");

        InitializeConfig(
        [
            new WorkflowConfigEntity
            {
                Id = "wf-1",
                Name = "Workflow 1",
                Description = "Demo",
                IsEnabled = true,
                NodesJson = JsonSerializer.Serialize(new[] { node }),
                EdgesJson = JsonSerializer.Serialize(new[] { edge }),
                EntryNodeId = "start",
                DefaultProviderId = "provider-a",
                CreatedAtMs = 100,
                UpdatedAtMs = 200,
            }
        ]);

        WorkflowStore store = new();

        WorkflowConfig workflow = store.All.Should().ContainSingle().Subject;
        workflow.Id.Should().Be("wf-1");
        workflow.Nodes.Should().ContainSingle();
        workflow.Edges.Should().ContainSingle();
        workflow.EntryNodeId.Should().Be("start");
        workflow.DefaultProviderId.Should().Be("provider-a");
        workflow.CreatedAtUtc.Should().Be(TimeUtils.FromMs(100));
        workflow.UpdatedAtUtc.Should().Be(TimeUtils.FromMs(200));
    }

    [Fact]
    public void Add_WhenCalled_PersistsWorkflowIntoConfigAndYaml()
    {
        InitializeConfig([]);
        WorkflowStore store = new();

        WorkflowConfig created = store.Add(new WorkflowConfig(
            Id: string.Empty,
            Name: "Created",
            Description: "New workflow",
            IsEnabled: true,
            Nodes: [],
            Edges: [],
            EntryNodeId: null,
            DefaultProviderId: "provider-a",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        WorkflowsOptions options = MicroClawConfig.Get<WorkflowsOptions>();
        options.Items.Should().ContainSingle(item => item.Id == created.Id && item.Name == "Created");

        string yaml = File.ReadAllText(Path.Combine(_configDir, "workflows.yaml"));
        yaml.Should().Contain("workflows:");
        yaml.Should().Contain("items:");
        yaml.Should().Contain($"id: {created.Id}");
    }

    [Fact]
    public void Update_WhenWorkflowExists_PreservesCreatedAtAndUpdatesFields()
    {
        InitializeConfig(
        [
            new WorkflowConfigEntity
            {
                Id = "wf-1",
                Name = "Old",
                Description = "Old description",
                IsEnabled = false,
                NodesJson = null,
                EdgesJson = null,
                EntryNodeId = "start",
                DefaultProviderId = "provider-a",
                CreatedAtMs = 123,
                UpdatedAtMs = 456,
            }
        ]);

        WorkflowStore store = new();

        WorkflowConfig? updated = store.Update("wf-1", new WorkflowConfig(
            Id: "ignored",
            Name: "New",
            Description: "New description",
            IsEnabled: true,
            Nodes: [],
            Edges: [],
            EntryNodeId: "entry",
            DefaultProviderId: "provider-b",
            CreatedAtUtc: DateTimeOffset.UtcNow,
            UpdatedAtUtc: DateTimeOffset.UtcNow));

        updated.Should().NotBeNull();
        updated!.Id.Should().Be("wf-1");
        updated.Name.Should().Be("New");
        updated.CreatedAtUtc.Should().Be(TimeUtils.FromMs(123));
        updated.UpdatedAtUtc.Should().BeAfter(updated.CreatedAtUtc);

        WorkflowsOptions options = MicroClawConfig.Get<WorkflowsOptions>();
        options.Items.Should().ContainSingle(item => item.Id == "wf-1" && item.Name == "New" && item.CreatedAtMs == 123);
    }

    [Fact]
    public void Delete_WhenWorkflowExists_RemovesWorkflowFromConfig()
    {
        InitializeConfig(
        [
            new WorkflowConfigEntity
            {
                Id = "wf-1",
                Name = "Workflow 1",
                Description = string.Empty,
                IsEnabled = true,
                CreatedAtMs = 100,
                UpdatedAtMs = 200,
            }
        ]);

        WorkflowStore store = new();

        bool deleted = store.Delete("wf-1");

        deleted.Should().BeTrue();
        MicroClawConfig.Get<WorkflowsOptions>().Items.Should().BeEmpty();
        store.GetById("wf-1").Should().BeNull();
    }

    public void Dispose()
    {
        MicroClawConfig.Reset();
        Environment.SetEnvironmentVariable("MICROCLAW_HOME", null);

        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private void InitializeConfig(WorkflowConfigEntity[] workflows)
    {
        Dictionary<string, string?> data = new()
        {
            ["workflows:items"] = null,
        };

        for (int i = 0; i < workflows.Length; i++)
        {
            WorkflowConfigEntity workflow = workflows[i];
            data[$"workflows:items:{i}:id"] = workflow.Id;
            data[$"workflows:items:{i}:name"] = workflow.Name;
            data[$"workflows:items:{i}:description"] = workflow.Description;
            data[$"workflows:items:{i}:is_enabled"] = workflow.IsEnabled.ToString();
            data[$"workflows:items:{i}:nodes_json"] = workflow.NodesJson;
            data[$"workflows:items:{i}:edges_json"] = workflow.EdgesJson;
            data[$"workflows:items:{i}:entry_node_id"] = workflow.EntryNodeId;
            data[$"workflows:items:{i}:default_provider_id"] = workflow.DefaultProviderId;
            data[$"workflows:items:{i}:created_at_ms"] = workflow.CreatedAtMs.ToString();
            data[$"workflows:items:{i}:updated_at_ms"] = workflow.UpdatedAtMs.ToString();
        }

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();

        MicroClawConfig.Initialize(configuration, _configDir);
    }
}