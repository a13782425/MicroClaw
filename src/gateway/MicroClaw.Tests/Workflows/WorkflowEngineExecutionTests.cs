using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Agent.ContextProviders;
using MicroClaw.Agent.Dev;
using MicroClaw.Agent.Memory;
using MicroClaw.Agent.Workflows;
using MicroClaw.Channels;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers;
using MicroClaw.Configuration;
using MicroClaw.Skills;
using MicroClaw.Tests.Fixtures;
using MicroClaw.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MicroClaw.Tests.Workflows;

/// <summary>
/// 工作流引擎执行测试：验证 Function / SwitchModel 节点、
/// 代理传播逻辑（默认 main）、以及 DefaultProviderId 的行为。
/// 不测试 Agent / Tool 节点的实际 LLM 调用（需完整 AgentRunner 上下文）。
/// </summary>
[Collection("Config")]
public sealed class WorkflowEngineExecutionTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly WorkflowEngine _engine;
    private readonly AgentStore _agentStore;
    private readonly ProviderConfigStore _providerStore;

    public WorkflowEngineExecutionTests()
    {
        TestConfigFixture.EnsureInitialized();
        string configDir = _tempDir.Path;

        _agentStore = new AgentStore();
        _providerStore = new ProviderConfigStore();

        // 确保存在默认 Agent（main）
        _agentStore.EnsureMainAgent();

        string agentsDir = Path.Combine(_tempDir.Path, "agents");
        var agentDna = new AgentDnaService(agentsDir);

        var skillService = new SkillService(_tempDir.Path);
        var skillStore = new SkillStore(skillService);
        var skillToolFactory = new SkillToolFactory(skillStore, skillService);
        var skillInvocationTool = new SkillInvocationTool(
            skillToolFactory,
            skillService,
            NullLoggerFactory.Instance.CreateLogger<SkillInvocationTool>(),
            subAgentRunner: null);

        var runner = new AgentRunner(
            agentStore: _agentStore,
            contextProviders: new IAgentContextProvider[]
            {
                new AgentDnaContextProvider(agentDna),
            },
            providerStore: _providerStore,
            clientFactory: CreateNoOpClientFactory(),
            sessionReader: Substitute.For<ISessionRepository>(),
            skillToolFactory: skillToolFactory,
            usageTracker: Substitute.For<IUsageTracker>(),
            loggerFactory: NullLoggerFactory.Instance,
            agentStatusNotifier: Substitute.For<IAgentStatusNotifier>(),
            toolCollector: new ToolCollector([], new McpServerConfigStore(configDir), NullLoggerFactory.Instance),
            devMetrics: Substitute.For<IDevMetricsService>(),
            contentPipeline: new MicroClaw.Agent.Streaming.AIContentPipeline([], NullLoggerFactory.Instance.CreateLogger<MicroClaw.Agent.Streaming.AIContentPipeline>()),
            chatContentRestorers: Array.Empty<MicroClaw.Agent.Restorers.IChatContentRestorer>());

        _engine = new WorkflowEngine(
            _agentStore,
            _providerStore,
            runner,
            NullLoggerFactory.Instance.CreateLogger<WorkflowEngine>());
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    // ── Function 节点测试 ─────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_FunctionUppercase_TransformsInput()
    {
        var wf = BuildWorkflow(
            new WorkflowNodeConfig("fn", "Uppercase", WorkflowNodeType.Function, null, "uppercase", null, null, null));

        var items = await CollectItems(wf, "hello world");

        items.Should().ContainSingle(i => i is TokenItem)
            .Which.As<TokenItem>().Content.Should().Be("HELLO WORLD");
    }

    [Fact]
    public async Task Execute_FunctionLowercase_TransformsInput()
    {
        var wf = BuildWorkflow(
            new WorkflowNodeConfig("fn", "Lowercase", WorkflowNodeType.Function, null, "lowercase", null, null, null));

        var items = await CollectItems(wf, "Hello World");

        items.Should().ContainSingle(i => i is TokenItem)
            .Which.As<TokenItem>().Content.Should().Be("hello world");
    }

    [Fact]
    public async Task Execute_FunctionTrim_TransformsInput()
    {
        var wf = BuildWorkflow(
            new WorkflowNodeConfig("fn", "Trim", WorkflowNodeType.Function, null, "trim", null, null, null));

        var items = await CollectItems(wf, "  spaced  ");

        items.Should().ContainSingle(i => i is TokenItem)
            .Which.As<TokenItem>().Content.Should().Be("spaced");
    }

    // ── SwitchModel 节点测试 ──────────────────────────────────────────────────

    [Fact]
    public async Task Execute_SwitchModel_EmitsModelSwitchItem()
    {
        var switchNode = new WorkflowNodeConfig(
            "sw", "Switch Model", WorkflowNodeType.SwitchModel, null, null, "provider-new", null, null);

        var wf = BuildWorkflow(switchNode);

        var items = await CollectItems(wf, "test input");

        items.Should().ContainSingle(i => i is WorkflowModelSwitchItem)
            .Which.As<WorkflowModelSwitchItem>().ProviderId.Should().Be("provider-new");
    }

    [Fact]
    public async Task Execute_SwitchModel_PassthroughInput()
    {
        // SwitchModel 节点不改变输入，只切换 provider
        var nodes = new WorkflowNodeConfig[]
        {
            new("start", "Start", WorkflowNodeType.Start, null, null, null, null, null),
            new("sw", "Switch", WorkflowNodeType.SwitchModel, null, null, "p1", null, null),
            new("fn", "Upper", WorkflowNodeType.Function, null, "uppercase", null, null, null),
            new("end", "End", WorkflowNodeType.End, null, null, null, null, null),
        };
        var edges = new WorkflowEdgeConfig[]
        {
            new("start", "sw", null, null),
            new("sw", "fn", null, null),
            new("fn", "end", null, null),
        };

        var wf = new WorkflowConfig("wf1", "Test", "", true, nodes, edges, "start", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var items = await CollectItems(wf, "hello");

        // SwitchModel 透传 → Function(uppercase) → "HELLO"
        var complete = items.OfType<WorkflowCompleteItem>().Single();
        complete.FinalResult.Should().Be("HELLO");
    }

    // ── 组合工作流测试 ────────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_ChainedFunctions_PipesOutputCorrectly()
    {
        var nodes = new WorkflowNodeConfig[]
        {
            new("start", "Start", WorkflowNodeType.Start, null, null, null, null, null),
            new("fn1", "Trim", WorkflowNodeType.Function, null, "trim", null, null, null),
            new("fn2", "Upper", WorkflowNodeType.Function, null, "uppercase", null, null, null),
            new("end", "End", WorkflowNodeType.End, null, null, null, null, null),
        };
        var edges = new WorkflowEdgeConfig[]
        {
            new("start", "fn1", null, null),
            new("fn1", "fn2", null, null),
            new("fn2", "end", null, null),
        };

        var wf = new WorkflowConfig("wf1", "Test", "", true, nodes, edges, "start", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var items = await CollectItems(wf, "  hello  ");

        var complete = items.OfType<WorkflowCompleteItem>().Single();
        complete.FinalResult.Should().Be("HELLO");
    }

    [Fact]
    public async Task Execute_EmptyWorkflow_ReturnsError()
    {
        var wf = new WorkflowConfig("wf1", "Empty", "", true, [], [], null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var items = await CollectItems(wf, "test");

        items.Should().ContainSingle(i => i is WorkflowErrorItem)
            .Which.As<WorkflowErrorItem>().Error.Should().Contain("没有可执行节点");
    }

    // ── 生命周期事件测试 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_ProducesStartAndCompleteItems()
    {
        var wf = BuildWorkflow(
            new WorkflowNodeConfig("fn", "Trim", WorkflowNodeType.Function, null, "trim", null, null, null));

        var items = await CollectItems(wf, "test");

        items.First().Should().BeOfType<WorkflowStartItem>();
        items.Last().Should().BeOfType<WorkflowCompleteItem>();
    }

    [Fact]
    public async Task Execute_ProducesNodeStartAndCompleteItems()
    {
        var wf = BuildWorkflow(
            new WorkflowNodeConfig("fn", "Upper", WorkflowNodeType.Function, null, "uppercase", null, null, null));

        var items = await CollectItems(wf, "test");

        items.Should().ContainSingle(i => i is WorkflowNodeStartItem)
            .Which.As<WorkflowNodeStartItem>().NodeId.Should().Be("fn");

        items.Should().ContainSingle(i => i is WorkflowNodeCompleteItem)
            .Which.As<WorkflowNodeCompleteItem>().NodeId.Should().Be("fn");
    }

    [Fact]
    public async Task Execute_ProducesEdgeItems()
    {
        var wf = BuildWorkflow(
            new WorkflowNodeConfig("fn", "Upper", WorkflowNodeType.Function, null, "uppercase", null, null, null));

        var items = await CollectItems(wf, "test");

        items.Should().ContainSingle(i => i is WorkflowEdgeItem)
            .Which.As<WorkflowEdgeItem>().SourceNodeId.Should().Be("start");
    }

    // ── DefaultProviderId 测试 ────────────────────────────────────────────────

    [Fact]
    public async Task Execute_WorkflowWithDefaultProviderId_UsedInModelSwitch()
    {
        // 验证 workflow.DefaultProviderId 被引擎读取（SwitchModel 覆盖后变化）
        var nodes = new WorkflowNodeConfig[]
        {
            new("start", "Start", WorkflowNodeType.Start, null, null, null, null, null),
            new("sw", "Switch", WorkflowNodeType.SwitchModel, null, null, "override-provider", null, null),
            new("fn", "Trim", WorkflowNodeType.Function, null, "trim", null, null, null),
            new("end", "End", WorkflowNodeType.End, null, null, null, null, null),
        };
        var edges = new WorkflowEdgeConfig[]
        {
            new("start", "sw", null, null),
            new("sw", "fn", null, null),
            new("fn", "end", null, null),
        };

        var wf = new WorkflowConfig("wf1", "Test", "", true, nodes, edges, "start",
            "default-provider-id", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var items = await CollectItems(wf, "test");

        // SwitchModel 应切换到 "override-provider"
        var switchItem = items.OfType<WorkflowModelSwitchItem>().Single();
        switchItem.ProviderId.Should().Be("override-provider");
    }

    // ── Router 节点测试 ──────────────────────────────────────────────────────

    [Fact]
    public async Task Execute_RouterNode_PassthroughInput()
    {
        var nodes = new WorkflowNodeConfig[]
        {
            new("start", "Start", WorkflowNodeType.Start, null, null, null, null, null),
            new("router", "Route", WorkflowNodeType.Router, null, null, null, null, null),
            new("fn", "Upper", WorkflowNodeType.Function, null, "uppercase", null, null, null),
            new("end", "End", WorkflowNodeType.End, null, null, null, null, null),
        };
        var edges = new WorkflowEdgeConfig[]
        {
            new("start", "router", null, null),
            new("router", "fn", null, null),
            new("fn", "end", null, null),
        };

        var wf = new WorkflowConfig("wf1", "Test", "", true, nodes, edges, "start", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        var items = await CollectItems(wf, "hello");

        var complete = items.OfType<WorkflowCompleteItem>().Single();
        complete.FinalResult.Should().Be("HELLO");
    }

    // ── 辅助方法 ──────────────────────────────────────────────────────────────

    /// <summary>构建 Start→node→End 简单工作流。</summary>
    private static WorkflowConfig BuildWorkflow(WorkflowNodeConfig middleNode)
    {
        var nodes = new WorkflowNodeConfig[]
        {
            new("start", "Start", WorkflowNodeType.Start, null, null, null, null, null),
            middleNode,
            new("end", "End", WorkflowNodeType.End, null, null, null, null, null),
        };
        var edges = new WorkflowEdgeConfig[]
        {
            new("start", middleNode.NodeId, null, null),
            new(middleNode.NodeId, "end", null, null),
        };
        return new WorkflowConfig("wf-test", "Test WF", "", true, nodes, edges, "start", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }

    private async Task<List<StreamItem>> CollectItems(WorkflowConfig wf, string input)
    {
        var items = new List<StreamItem>();
        await foreach (StreamItem item in _engine.ExecuteAsync(wf, input, "exec-test"))
            items.Add(item);
        return items;
    }

    private static ProviderClientFactory CreateNoOpClientFactory()
    {
        var mockProvider = Substitute.For<IModelProvider>();
        var mockClient = Substitute.For<IChatClient>();
        mockProvider.Supports(Arg.Any<ProviderProtocol>()).Returns(true);
        mockProvider.Create(Arg.Any<ProviderConfig>()).Returns(mockClient);
        return new ProviderClientFactory([mockProvider]);
    }
}
