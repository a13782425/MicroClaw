using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Agent.ContextProviders;
using MicroClaw.Agent.Memory;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// 验证三个内置 IAgentContextProvider 实现的行为：
/// AgentDnaContextProvider、SessionDnaContextProvider、SessionMemoryContextProvider。
/// </summary>
public sealed class ContextProviderTests : IDisposable
{
    private const string AgentId = "ctx-provider-agent-001";
    private const string SessionId = "ctx-provider-session-001";

    private readonly TempDirectoryFixture _tempDir = new();

    private readonly AgentConfig _agent;
    private readonly AgentDnaService _agentDna;
    private readonly SessionDnaService _sessionDna;
    private readonly MemoryService _memory;

    public ContextProviderTests()
    {
        string agentsDir = Path.Combine(_tempDir.Path, "agents");
        _agentDna = new AgentDnaService(agentsDir);
        _sessionDna = new SessionDnaService(_tempDir.Path);
        _memory = new MemoryService(_tempDir.Path);

        _agent = new AgentConfig(
            Id: AgentId,
            Name: "Provider Test Agent",
            Description: "",
            IsEnabled: true,
            BoundSkillIds: [],
            EnabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow);
    }

    public void Dispose() => _tempDir.Dispose();

    // ── AgentDnaContextProvider ───────────────────────────────────────────────

    [Fact]
    public void AgentDnaContextProvider_Order_Is10()
    {
        var provider = new AgentDnaContextProvider(_agentDna);
        provider.Order.Should().Be(10);
    }

    [Fact]
    public async Task AgentDnaContextProvider_AgentWithNoFiles_ReturnsNull()
    {
        var provider = new AgentDnaContextProvider(_agentDna);

        string? result = await provider.BuildContextAsync(_agent, sessionId: null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task AgentDnaContextProvider_AgentWithSoul_ReturnsSoulContent()
    {
        _agentDna.InitializeAgent(AgentId);
        _agentDna.UpdateSoul(AgentId, "# Soul\nAGENT-DNA-SOUL");

        var provider = new AgentDnaContextProvider(_agentDna);
        string? result = await provider.BuildContextAsync(_agent, sessionId: null);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("AGENT-DNA-SOUL");
    }

    [Fact]
    public async Task AgentDnaContextProvider_IgnoresSessionId_AlwaysReadsAgentFiles()
    {
        _agentDna.InitializeAgent(AgentId);
        _agentDna.UpdateSoul(AgentId, "AGENT-SOUL-MARKER");

        var provider = new AgentDnaContextProvider(_agentDna);

        // sessionId 无论是 null 还是有值，Agent DNA 都应该被读取
        string? resultNull = await provider.BuildContextAsync(_agent, sessionId: null);
        string? resultWithSession = await provider.BuildContextAsync(_agent, sessionId: "some-session");

        resultNull.Should().Contain("AGENT-SOUL-MARKER");
        resultWithSession.Should().Contain("AGENT-SOUL-MARKER");
    }

    [Fact]
    public async Task AgentDnaContextProvider_AgentWithMemory_IncludesMemorySection()
    {
        _agentDna.InitializeAgent(AgentId);
        _agentDna.AppendMemory(AgentId, "AGENT-MEMORY-ENTRY");

        var provider = new AgentDnaContextProvider(_agentDna);
        string? result = await provider.BuildContextAsync(_agent, sessionId: null);

        result.Should().Contain("AGENT-MEMORY-ENTRY");
    }

    // ── SessionDnaContextProvider ─────────────────────────────────────────────

    [Fact]
    public void SessionDnaContextProvider_Order_Is20()
    {
        var provider = new SessionDnaContextProvider(_sessionDna);
        provider.Order.Should().Be(20);
    }

    [Fact]
    public async Task SessionDnaContextProvider_NullSessionId_ReturnsNull()
    {
        var provider = new SessionDnaContextProvider(_sessionDna);

        string? result = await provider.BuildContextAsync(_agent, sessionId: null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SessionDnaContextProvider_EmptySessionId_ReturnsNull()
    {
        var provider = new SessionDnaContextProvider(_sessionDna);

        string? result = await provider.BuildContextAsync(_agent, sessionId: string.Empty);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SessionDnaContextProvider_SessionWithNoFiles_ReturnsNull()
    {
        var provider = new SessionDnaContextProvider(_sessionDna);

        string? result = await provider.BuildContextAsync(_agent, sessionId: SessionId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SessionDnaContextProvider_SessionWithInitializedFiles_ReturnsContent()
    {
        _sessionDna.InitializeSession(SessionId);
        var provider = new SessionDnaContextProvider(_sessionDna);

        string? result = await provider.BuildContextAsync(_agent, sessionId: SessionId);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("User");
    }

    [Fact]
    public async Task SessionDnaContextProvider_SessionWithCustomContent_ReturnsCustomContent()
    {
        _sessionDna.InitializeSession(SessionId);
        _sessionDna.Update(SessionId, "USER.md", "SESSION-USER-CUSTOM");

        var provider = new SessionDnaContextProvider(_sessionDna);
        string? result = await provider.BuildContextAsync(_agent, sessionId: SessionId);

        result.Should().Contain("SESSION-USER-CUSTOM");
    }

    [Fact]
    public async Task SessionDnaContextProvider_BothFilesEmpty_ReturnsNull()
    {
        _sessionDna.InitializeSession(SessionId);
        _sessionDna.Update(SessionId, "USER.md", "");
        _sessionDna.Update(SessionId, "AGENTS.md", "  ");

        var provider = new SessionDnaContextProvider(_sessionDna);
        string? result = await provider.BuildContextAsync(_agent, sessionId: SessionId);

        result.Should().BeNull();
    }

    // ── SessionMemoryContextProvider ─────────────────────────────────────────

    [Fact]
    public void SessionMemoryContextProvider_Order_Is30()
    {
        var provider = new SessionMemoryContextProvider(_memory);
        provider.Order.Should().Be(30);
    }

    [Fact]
    public async Task SessionMemoryContextProvider_NullSessionId_ReturnsNull()
    {
        var provider = new SessionMemoryContextProvider(_memory);

        string? result = await provider.BuildContextAsync(_agent, sessionId: null);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SessionMemoryContextProvider_EmptySessionId_ReturnsNull()
    {
        var provider = new SessionMemoryContextProvider(_memory);

        string? result = await provider.BuildContextAsync(_agent, sessionId: string.Empty);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SessionMemoryContextProvider_NoMemoryFiles_ReturnsNull()
    {
        var provider = new SessionMemoryContextProvider(_memory);

        string? result = await provider.BuildContextAsync(_agent, sessionId: SessionId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SessionMemoryContextProvider_WithLongTermMemory_ReturnsContent()
    {
        _memory.UpdateLongTermMemory(SessionId, "LONG-TERM-MEMORY-ENTRY");

        var provider = new SessionMemoryContextProvider(_memory);
        string? result = await provider.BuildContextAsync(_agent, sessionId: SessionId);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("LONG-TERM-MEMORY-ENTRY");
    }

    [Fact]
    public async Task SessionMemoryContextProvider_WithDailyMemory_ReturnsContent()
    {
        string today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        _memory.WriteDailyMemory(SessionId, today, "DAILY-MEMORY-ENTRY");

        var provider = new SessionMemoryContextProvider(_memory);
        string? result = await provider.BuildContextAsync(_agent, sessionId: SessionId);

        result.Should().NotBeNullOrEmpty();
        result.Should().Contain("DAILY-MEMORY-ENTRY");
    }

    // ── Provider 顺序与组合 ───────────────────────────────────────────────────

    [Fact]
    public void ProviderOrders_AreStrictlyAscending()
    {
        IAgentContextProvider[] providers =
        [
            new AgentDnaContextProvider(_agentDna),
            new SessionDnaContextProvider(_sessionDna),
            new SessionMemoryContextProvider(_memory),
        ];

        var sorted = providers.OrderBy(p => p.Order).ToList();

        sorted[0].Should().BeOfType<AgentDnaContextProvider>("AgentDna Order=10 应最小");
        sorted[1].Should().BeOfType<SessionDnaContextProvider>("SessionDna Order=20 居中");
        sorted[2].Should().BeOfType<SessionMemoryContextProvider>("Memory Order=30 应最大");
    }

    [Fact]
    public void AllProviders_HaveUniqueOrders()
    {
        IAgentContextProvider[] providers =
        [
            new AgentDnaContextProvider(_agentDna),
            new SessionDnaContextProvider(_sessionDna),
            new SessionMemoryContextProvider(_memory),
        ];

        var orders = providers.Select(p => p.Order).ToList();
        orders.Should().OnlyHaveUniqueItems("每个 Provider 的 Order 值必须唯一，避免注入顺序不确定");
    }
}
