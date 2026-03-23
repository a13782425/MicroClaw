using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Sessions;
using MicroClaw.Tests.Fixtures;
using MicroClaw.Tools;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// 测试 SubAgentRunnerService 的深度限制与循环调用检测逻辑。
/// 测试不调用 AgentRunner（深度/循环校验在创建子会话前即抛出），
/// 因此使用占位 Lazy&lt;AgentRunner&gt;，若被意外访问则抛出异常。
/// </summary>
public sealed class SubAgentRunnerServiceTests : IDisposable
{
    private readonly DatabaseFixture _db = new();
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly SessionStore _sessionStore;
    private readonly AgentStore _agentStore;
    private readonly SubAgentRunnerService _service;

    // 深度/循环测试不应到达 AgentRunner，若意外访问则测试失败
    private static readonly Lazy<AgentRunner> UnreachableRunner =
        new(() => throw new InvalidOperationException("AgentRunner should not be reached in this test."));

    public SubAgentRunnerServiceTests()
    {
        _sessionStore = new SessionStore(_db.CreateFactory(), _tempDir.Path);
        _agentStore = new AgentStore(_db.CreateFactory());
        _service = new SubAgentRunnerService(_sessionStore, _agentStore, UnreachableRunner);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
        _db.Dispose();
    }

    // ── 辅助方法 ─────────────────────────────────────────────────────────────

    private AgentConfig AddAgent(string name, bool isEnabled = true) =>
        _agentStore.Add(new AgentConfig(
            Id: string.Empty,
            Name: name,
            Description: "You are a helpful assistant.",
            IsEnabled: isEnabled,
            BoundSkillIds: [],
            EnabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow));

    /// <summary>
    /// 创建会话链：root（无父）→ level1（parent=root）→ ... → levelN。
    /// 返回最深层会话的 ID。
    /// </summary>
    private string BuildSessionChain(int subAgentLevels, string agentId)
    {
        // 顶层用户会话（无 ParentSessionId）
        SessionInfo root = _sessionStore.Create("root", "provider-1");
        string current = root.Id;

        for (int i = 0; i < subAgentLevels; i++)
        {
            SessionInfo sub = _sessionStore.Create(
                $"[子代理] level-{i + 1}", "provider-1", ChannelType.Web,
                agentId: agentId,
                parentSessionId: current);
            current = sub.Id;
        }

        return current;
    }

    // ── 深度限制测试 ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunSubAgentAsync_Depth1_IsAllowed()
    {
        var agentA = AddAgent("AgentA");
        var agentB = AddAgent("AgentB");

        // 从顶层用户会话（depth=0）调用，创建 depth-1 子代理
        SessionInfo root = _sessionStore.Create("root", "provider-1");

        // 期待调用失败于 AgentRunner（因为 Lazy 会抛出），但不应提前因深度检查抛出
        Func<Task> act = () => _service.RunSubAgentAsync(agentB.Id, "task", root.Id);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("AgentRunner should not be reached in this test.");
    }

    [Fact]
    public async Task RunSubAgentAsync_Depth2_IsAllowed()
    {
        var agentA = AddAgent("AgentA");
        var agentB = AddAgent("AgentB");

        // root → S1(agentA) → 尝试创建 S2(agentB)，depth-2 应被允许
        string level1Id = BuildSessionChain(1, agentA.Id);

        Func<Task> act = () => _service.RunSubAgentAsync(agentB.Id, "task", level1Id);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("AgentRunner should not be reached in this test.");
    }

    [Fact]
    public async Task RunSubAgentAsync_Depth3_IsAllowed()
    {
        var agentA = AddAgent("AgentA");
        var agentB = AddAgent("AgentB");
        var agentC = AddAgent("AgentC");
        var agentD = AddAgent("AgentD");

        // root → S1(agentA) → S2(agentB) → 尝试创建 S3(agentC)，depth-3 应被允许
        string level2Id = BuildSessionChain(2, agentA.Id);

        Func<Task> act = () => _service.RunSubAgentAsync(agentC.Id, "task", level2Id);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("AgentRunner should not be reached in this test.");
    }

    [Fact]
    public async Task RunSubAgentAsync_Depth4_ThrowsDepthExceeded()
    {
        var agentA = AddAgent("AgentA");
        var agentB = AddAgent("AgentB");

        // root → S1 → S2 → S3，尝试创建 S4（depth-4），应被拒绝
        string level3Id = BuildSessionChain(3, agentA.Id);

        Func<Task> act = () => _service.RunSubAgentAsync(agentB.Id, "task", level3Id);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*深度已达上限*");
    }

    [Fact]
    public async Task RunSubAgentAsync_Depth5_ThrowsDepthExceeded()
    {
        var agentA = AddAgent("AgentA");
        var agentB = AddAgent("AgentB");

        // 超过上限更多层时同样应被拒绝
        string level4Id = BuildSessionChain(4, agentA.Id);

        Func<Task> act = () => _service.RunSubAgentAsync(agentB.Id, "task", level4Id);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*深度已达上限*");
    }

    // ── 循环调用检测测试 ───────────────────────────────────────────────────────

    [Fact]
    public async Task RunSubAgentAsync_DirectCycle_ThrowsCycleDetected()
    {
        // A 正在执行（session 中 agentId=A），A 尝试调用自身 → A→A 循环
        var agentA = AddAgent("AgentA");
        SessionInfo root = _sessionStore.Create("root", "provider-1");
        SessionInfo s1 = _sessionStore.Create("[子代理] A", "provider-1", ChannelType.Web,
            agentId: agentA.Id, parentSessionId: root.Id);

        // 从 s1 尝试再次调用 agentA → 应检测到循环
        Func<Task> act = () => _service.RunSubAgentAsync(agentA.Id, "task", s1.Id);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*循环子代理调用*");
    }

    [Fact]
    public async Task RunSubAgentAsync_IndirectCycle_ThrowsCycleDetected()
    {
        // A → B → A（间接循环）
        var agentA = AddAgent("AgentA");
        var agentB = AddAgent("AgentB");

        SessionInfo root = _sessionStore.Create("root", "provider-1");
        SessionInfo s1 = _sessionStore.Create("[子代理] A", "provider-1", ChannelType.Web,
            agentId: agentA.Id, parentSessionId: root.Id);
        SessionInfo s2 = _sessionStore.Create("[子代理] B", "provider-1", ChannelType.Web,
            agentId: agentB.Id, parentSessionId: s1.Id);

        // 从 s2（agentB 执行中）尝试调用 agentA → A 在祖先链中
        Func<Task> act = () => _service.RunSubAgentAsync(agentA.Id, "task", s2.Id);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*循环子代理调用*");
    }

    [Fact]
    public async Task RunSubAgentAsync_DifferentAgentsNoOverlap_IsAllowed()
    {
        // A → B → C（无循环），从 s2 调用 agentC（未出现在链中）应通过深度/循环检测
        var agentA = AddAgent("AgentA");
        var agentB = AddAgent("AgentB");
        var agentC = AddAgent("AgentC");

        SessionInfo root = _sessionStore.Create("root", "provider-1");
        SessionInfo s1 = _sessionStore.Create("[子代理] A", "provider-1", ChannelType.Web,
            agentId: agentA.Id, parentSessionId: root.Id);
        SessionInfo s2 = _sessionStore.Create("[子代理] B", "provider-1", ChannelType.Web,
            agentId: agentB.Id, parentSessionId: s1.Id);

        // agentC 不在链中，应通过检测（失败于 AgentRunner 而非深度/循环检测）
        Func<Task> act = () => _service.RunSubAgentAsync(agentC.Id, "task", s2.Id);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("AgentRunner should not be reached in this test.");
    }

    // ── 基础校验测试 ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RunSubAgentAsync_AgentNotFound_Throws()
    {
        SessionInfo root = _sessionStore.Create("root", "provider-1");

        Func<Task> act = () => _service.RunSubAgentAsync("nonexistent-id", "task", root.Id);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*不存在*");
    }

    [Fact]
    public async Task RunSubAgentAsync_DisabledAgent_Throws()
    {
        var disabled = AddAgent("Disabled", isEnabled: false);
        SessionInfo root = _sessionStore.Create("root", "provider-1");

        Func<Task> act = () => _service.RunSubAgentAsync(disabled.Id, "task", root.Id);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*未启用*");
    }
}
