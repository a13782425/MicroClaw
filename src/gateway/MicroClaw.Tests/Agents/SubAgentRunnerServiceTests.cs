using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Hubs;
using MicroClaw.Sessions;
using MicroClaw.Tests.Fixtures;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace MicroClaw.Tests.Agents;

public sealed class SubAgentRunnerServiceTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly SessionService _svc;
    private readonly AgentStore _agentStore;
    private readonly SubAgentRunnerService _service;

    private static readonly Lazy<AgentRunner> UnreachableRunner =
        new(() => throw new InvalidOperationException("AgentRunner should not be reached in this test."));

    public SubAgentRunnerServiceTests()
    {
        TestConfigFixture.EnsureInitialized();

        var hubContext = Substitute.For<IHubContext<GatewayHub>>();
        var clients = Substitute.For<IHubClients>();
        hubContext.Clients.Returns(clients);
        clients.All.Returns(Substitute.For<IClientProxy>());

        _agentStore = new AgentStore();
        var webChannel = new WebChannel(hubContext);
        _svc = new SessionService(_agentStore, hubContext, [], webChannel, _tempDir.Path);
        _service = new SubAgentRunnerService(_svc, _agentStore, UnreachableRunner);
    }

    public void Dispose()
    {
        SubAgentRunScope.Current = null;
        _tempDir.Dispose();
    }

    private AgentConfig AddAgent(string name, bool isEnabled = true) =>
        _agentStore.Add(new AgentConfig(
            Id: string.Empty,
            Name: name,
            Description: "You are a helpful assistant.",
            IsEnabled: isEnabled,
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow));

    private string CreateRootSession() => _svc.CreateSession("root", "provider-1").Id;

    [Fact]
    public async Task RunSubAgentAsync_WithoutExistingRunScope_IsAllowed()
    {
        var agentB = AddAgent("AgentB");
        string rootId = CreateRootSession();

        Func<Task> act = () => _service.RunSubAgentAsync(agentB.Id, "task", rootId);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("AgentRunner should not be reached in this test.");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task RunSubAgentAsync_WithinDepthLimit_IsAllowed(int chainDepth)
    {
        var agentA = AddAgent("AgentA");
        var agentB = AddAgent("AgentB");
        string rootId = CreateRootSession();

        SubAgentRunScope.Current = new SubAgentRunContext(
            rootId,
            Enumerable.Repeat(agentA.Id, chainDepth).ToArray());

        Func<Task> act = () => _service.RunSubAgentAsync(agentB.Id, "task", rootId);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("AgentRunner should not be reached in this test.");
    }

    [Fact]
    public async Task RunSubAgentAsync_Depth4_ThrowsDepthExceeded()
    {
        var agentA = AddAgent("AgentA");
        var agentB = AddAgent("AgentB");
        string rootId = CreateRootSession();

        SubAgentRunScope.Current = new SubAgentRunContext(rootId, [agentA.Id, agentA.Id, agentA.Id, agentA.Id]);

        Func<Task> act = () => _service.RunSubAgentAsync(agentB.Id, "task", rootId);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*深度已达上限*");
    }

    [Fact]
    public async Task RunSubAgentAsync_DirectCycle_ThrowsCycleDetected()
    {
        var agentA = AddAgent("AgentA");
        string rootId = CreateRootSession();

        SubAgentRunScope.Current = new SubAgentRunContext(rootId, [agentA.Id]);

        Func<Task> act = () => _service.RunSubAgentAsync(agentA.Id, "task", rootId);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*循环子代理调用*");
    }

    [Fact]
    public async Task RunSubAgentAsync_IndirectCycle_ThrowsCycleDetected()
    {
        var agentA = AddAgent("AgentA");
        var agentB = AddAgent("AgentB");
        string rootId = CreateRootSession();

        SubAgentRunScope.Current = new SubAgentRunContext(rootId, [agentA.Id, agentB.Id]);

        Func<Task> act = () => _service.RunSubAgentAsync(agentA.Id, "task", rootId);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*循环子代理调用*");
    }

    [Fact]
    public async Task RunSubAgentAsync_DifferentAgentsNoOverlap_IsAllowed()
    {
        var agentA = AddAgent("AgentA");
        var agentB = AddAgent("AgentB");
        var agentC = AddAgent("AgentC");
        string rootId = CreateRootSession();

        SubAgentRunScope.Current = new SubAgentRunContext(rootId, [agentA.Id, agentB.Id]);

        Func<Task> act = () => _service.RunSubAgentAsync(agentC.Id, "task", rootId);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("AgentRunner should not be reached in this test.");
    }

    [Fact]
    public async Task RunSubAgentAsync_AgentNotFound_Throws()
    {
        string rootId = CreateRootSession();

        Func<Task> act = () => _service.RunSubAgentAsync("nonexistent-id", "task", rootId);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*不存在*");
    }

    [Fact]
    public async Task RunSubAgentAsync_DisabledAgent_Throws()
    {
        var disabled = AddAgent("Disabled", isEnabled: false);
        string rootId = CreateRootSession();

        Func<Task> act = () => _service.RunSubAgentAsync(disabled.Id, "task", rootId);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*未启用*");
    }
}
