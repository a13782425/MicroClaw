using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Agent.ContextProviders;
using MicroClaw.Agent.Dev;
using MicroClaw.Agent.Memory;
using MicroClaw.Channels;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Infrastructure;
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

namespace MicroClaw.Tests.Agents;

/// <summary>
/// 验证 AgentRunner.BuildSystemPromptAsync — 通过 IAgentContextProvider 聚合上下文片段。
/// System Prompt 由 AgentDnaContextProvider + SessionDnaContextProvider + SessionMemoryContextProvider 按 Order 顺序构成。
/// </summary>
[Collection("Config")]
public sealed class AgentRunnerBuildPromptTests : IDisposable
{
    private const string SessionId = "test-session-001";

    private readonly TempDirectoryFixture _tempDir = new();

    private readonly SessionDnaService _sessionDna;
    private readonly MemoryService _memory;
    private readonly AgentDnaService _agentDna;
    private readonly AgentRunner _runner;
    private readonly AgentConfig _testAgent;

    public AgentRunnerBuildPromptTests()
    {
        TestConfigFixture.EnsureInitialized();
        _sessionDna = new SessionDnaService(_tempDir.Path);
        _memory = new MemoryService(_tempDir.Path);

        string agentsDir = Path.Combine(_tempDir.Path, "agents");
        _agentDna = new AgentDnaService(agentsDir);

        string configDir = _tempDir.Path;

        var skillService = new SkillService(_tempDir.Path);
        var skillStore = new SkillStore(skillService);
        var skillToolFactory = new SkillToolFactory(skillStore, skillService);
        var skillInvocationTool = new SkillInvocationTool(
            skillToolFactory,
            skillService,
            NullLoggerFactory.Instance.CreateLogger<SkillInvocationTool>(),
            subAgentRunner: null);

        var agentStore = new AgentStore();

        // F9：使用 Context Provider 替代直接注入三个服务
        var contextProviders = new IAgentContextProvider[]
        {
            new AgentDnaContextProvider(_agentDna),
            new SessionDnaContextProvider(_sessionDna),
            new SessionMemoryContextProvider(_memory),
        };

        _runner = new AgentRunner(
            agentStore:            agentStore,
            contextProviders:      contextProviders,
            providerStore:         new ProviderConfigStore(),
            clientFactory:         CreateNoOpClientFactory(),
            sessionReader:         Substitute.For<ISessionReader>(),
            skillToolFactory:      skillToolFactory,
            usageTracker:          Substitute.For<IUsageTracker>(),
            loggerFactory:         NullLoggerFactory.Instance,
            agentStatusNotifier:   Substitute.For<IAgentStatusNotifier>(),
            toolCollector:         new ToolCollector([], new McpServerConfigStore(configDir), NullLoggerFactory.Instance),
            devMetrics:            Substitute.For<IDevMetricsService>(),
            contentPipeline:       new MicroClaw.Agent.Streaming.AIContentPipeline([], NullLoggerFactory.Instance.CreateLogger<MicroClaw.Agent.Streaming.AIContentPipeline>()),
            chatContentRestorers:  Array.Empty<MicroClaw.Agent.Restorers.IChatContentRestorer>());

        // 初始化测试 Agent
        _testAgent = new AgentConfig(
            Id: "test-agent-001",
            Name: "Test Agent",
            Description: "",
            IsEnabled: true,
            DisabledSkillIds: [],
            DisabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow);
        _agentDna.InitializeAgent(_testAgent.Id);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    // ── 无 sessionId 场景 ──────────────────────────────────────────────────────

    [Fact]
    public async Task BuildSystemPromptAsync_NullSessionId_ReturnsAgentDnaOnly()
    {
        // sessionId 为 null 时，仅包含 Agent 的 SOUL（Agent DNA 不依赖 sessionId）
        string prompt = await _runner.BuildSystemPromptAsync(_testAgent, null);

        // Agent SOUL 默认模板包含 "Soul" 标题
        prompt.Should().Contain("Soul");
    }

    [Fact]
    public async Task BuildSystemPromptAsync_EmptySessionId_ReturnsAgentDnaOnly()
    {
        string prompt = await _runner.BuildSystemPromptAsync(_testAgent, string.Empty);
        prompt.Should().Contain("Soul");
    }

    // ── Session DNA 场景 ──────────────────────────────────────────────────────

    [Fact]
    public async Task BuildSystemPromptAsync_NoSessionFilesExist_ReturnsAgentDnaOnly()
    {
        // Session 文件未初始化，Session DNA Provider 返回 null，只有 Agent DNA
        string prompt = await _runner.BuildSystemPromptAsync(_testAgent, SessionId);

        prompt.Should().Contain("Soul");
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithSessionDnaFilesInitialized_ContainsUserAndAgents()
    {
        _sessionDna.InitializeSession(SessionId);

        string prompt = await _runner.BuildSystemPromptAsync(_testAgent, SessionId);

        prompt.Should().Contain("User");
        prompt.Should().Contain("Agents");
    }

    // ── Agent SOUL 自定义内容 ─────────────────────────────────────────────────

    [Fact]
    public async Task BuildSystemPromptAsync_WithCustomAgentSoul_IncludesSoulContent()
    {
        _agentDna.UpdateSoul(_testAgent.Id, "# Soul\n自定义人格测试内容");

        string prompt = await _runner.BuildSystemPromptAsync(_testAgent, SessionId);

        prompt.Should().Contain("自定义人格测试内容");
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithAllDnaFiles_IncludesAllContent()
    {
        _agentDna.UpdateSoul(_testAgent.Id, "AGENT-SOUL-CONTENT");
        _agentDna.AppendMemory(_testAgent.Id, "AGENT-MEMORY-CONTENT");
        _sessionDna.InitializeSession(SessionId);
        _sessionDna.Update(SessionId, "USER.md", "USER-CONTENT");
        _sessionDna.Update(SessionId, "AGENTS.md", "AGENTS-CONTENT");

        string prompt = await _runner.BuildSystemPromptAsync(_testAgent, SessionId);

        prompt.Should().Contain("AGENT-SOUL-CONTENT");
        prompt.Should().Contain("AGENT-MEMORY-CONTENT");
        prompt.Should().Contain("USER-CONTENT");
        prompt.Should().Contain("AGENTS-CONTENT");
    }

    // ── 记忆场景 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildSystemPromptAsync_WithLongTermMemory_IncludesMemoryContent()
    {
        _memory.UpdateLongTermMemory(SessionId, "# Long Term Memory\n长期记忆测试");

        string prompt = await _runner.BuildSystemPromptAsync(_testAgent, SessionId);

        prompt.Should().Contain("长期记忆测试");
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithRecentDailyMemory_IncludesFullDailyContent()
    {
        string today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        _memory.WriteDailyMemory(SessionId, today, "今日 API 测试内容");

        string prompt = await _runner.BuildSystemPromptAsync(_testAgent, SessionId);

        prompt.Should().Contain("今日 API 测试内容");
    }

    // ── 组合场景 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildSystemPromptAsync_WithDnaAndMemory_IncludesBoth()
    {
        _agentDna.UpdateSoul(_testAgent.Id, "# Soul\nDNA-MARKER");
        _memory.UpdateLongTermMemory(SessionId, "# Memory\nMEMORY-MARKER");

        string prompt = await _runner.BuildSystemPromptAsync(_testAgent, SessionId);

        prompt.Should().Contain("DNA-MARKER");
        prompt.Should().Contain("MEMORY-MARKER");
    }

    [Fact]
    public async Task BuildSystemPromptAsync_AgentDnaAppearsBeforeSessionMemory_InCorrectOrder()
    {
        _agentDna.UpdateSoul(_testAgent.Id, "AGENT-DNA-SECTION");
        _memory.UpdateLongTermMemory(SessionId, "MEMORY-SECTION");

        string prompt = await _runner.BuildSystemPromptAsync(_testAgent, SessionId);

        int dnaIndex = prompt.IndexOf("AGENT-DNA-SECTION", StringComparison.Ordinal);
        int memIndex = prompt.IndexOf("MEMORY-SECTION", StringComparison.Ordinal);

        dnaIndex.Should().BeLessThan(memIndex,
            "Agent DNA 需要在 Session 记忆之前注入（Provider Order：Agent DNA < Session DNA < Memory）");
    }

    [Fact]
    public async Task BuildSystemPromptAsync_EmptySessionDnaFiles_StillContainsAgentDna()
    {
        // Session DNA 文件内容为空
        _sessionDna.InitializeSession(SessionId);
        _sessionDna.Update(SessionId, "USER.md", "");
        _sessionDna.Update(SessionId, "AGENTS.md", "  \n  ");

        // 只应包含 Agent SOUL 内容
        string prompt = await _runner.BuildSystemPromptAsync(_testAgent, SessionId);

        // Agent 的默认 SOUL 模板存在
        prompt.Should().Contain("Soul");
    }

    // ── 私有辅助方法 ──────────────────────────────────────────────────────────

    private static ProviderClientFactory CreateNoOpClientFactory()
    {
        var mockProvider = Substitute.For<IModelProvider>();
        var mockClient = Substitute.For<IChatClient>();
        mockProvider.Supports(Arg.Any<ProviderProtocol>()).Returns(true);
        mockProvider.Create(Arg.Any<ProviderConfig>()).Returns(mockClient);
        return new ProviderClientFactory([mockProvider]);
    }
}
