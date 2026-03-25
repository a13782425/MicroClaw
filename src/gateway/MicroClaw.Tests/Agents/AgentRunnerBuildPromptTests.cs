using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Agent.Memory;
using MicroClaw.Channels;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers;
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
/// ?? AgentRunner.BuildSystemPrompt?
/// System Prompt ? Agent DNA?SOUL/MEMORY?+ Session DNA?USER/AGENTS?+ Session ?? ???
/// </summary>
public sealed class AgentRunnerBuildPromptTests : IDisposable
{
    private const string SessionId = "test-session-001";

    private readonly DatabaseFixture _db = new();
    private readonly TempDirectoryFixture _tempDir = new();

    private readonly SessionDnaService _sessionDna;
    private readonly MemoryService _memory;
    private readonly AgentDnaService _agentDna;
    private readonly AgentRunner _runner;
    private readonly AgentConfig _testAgent;

    public AgentRunnerBuildPromptTests()
    {
        _sessionDna = new SessionDnaService(_tempDir.Path);
        _memory = new MemoryService(_tempDir.Path);

        string agentsDir = Path.Combine(_tempDir.Path, "agents");
        _agentDna = new AgentDnaService(agentsDir);

        IDbContextFactory<GatewayDbContext> dbFactory = _db.CreateFactory();

        var skillService = new SkillService(_tempDir.Path);
        var skillStore = new SkillStore(dbFactory);
        var skillOptions = Microsoft.Extensions.Options.Options.Create(new SkillOptions());
        var skillToolFactory = new SkillToolFactory(skillStore, skillService, skillOptions);
        var skillInvocationTool = new SkillInvocationTool(
            skillToolFactory,
            skillService,
            new SkillOptions(),
            NullLoggerFactory.Instance.CreateLogger<SkillInvocationTool>(),
            subAgentRunner: null);

        var agentStore = new AgentStore(dbFactory);

        _runner = new AgentRunner(
            agentStore:            agentStore,
            agentDnaService:       _agentDna,
            sessionDnaService:     _sessionDna,
            memoryService:         _memory,
            mcpServerConfigStore:  new McpServerConfigStore(dbFactory),
            providerStore:         new ProviderConfigStore(dbFactory),
            clientFactory:         CreateNoOpClientFactory(),
            sessionReader:         Substitute.For<ISessionReader>(),
            skillToolFactory:      skillToolFactory,
            skillInvocationTool:   skillInvocationTool,
            usageTracker:          Substitute.For<IUsageTracker>(),
            loggerFactory:         NullLoggerFactory.Instance,
            agentStatusNotifier:   Substitute.For<IAgentStatusNotifier>(),
            channelConfigStore:    new ChannelConfigStore(dbFactory),
            toolProviders:         [],
            builtinToolProviders:  []);

        // ????? Agent
        _testAgent = new AgentConfig(
            Id: "test-agent-001",
            Name: "Test Agent",
            Description: "",
            IsEnabled: true,
            BoundSkillIds: [],
            EnabledMcpServerIds: [],
            ToolGroupConfigs: [],
            CreatedAtUtc: DateTimeOffset.UtcNow);
        _agentDna.InitializeAgent(_testAgent.Id);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
        _db.Dispose();
    }

    // ?? BuildSystemPrompt?? sessionId ??????????????????????????????????????

    [Fact]
    public void BuildSystemPrompt_NullSessionId_ReturnsAgentDnaOnly()
    {
        // sessionId ? null ????? Agent ? SOUL?Agent DNA ??? sessionId?
        string prompt = _runner.BuildSystemPrompt(_testAgent, null);

        // Agent SOUL ?????? "Soul" ??
        prompt.Should().Contain("Soul");
    }

    [Fact]
    public void BuildSystemPrompt_EmptySessionId_ReturnsAgentDnaOnly()
    {
        string prompt = _runner.BuildSystemPrompt(_testAgent, string.Empty);
        prompt.Should().Contain("Soul");
    }

    // ?? BuildSystemPrompt?? Session DNA ?? ????????????????????????????????

    [Fact]
    public void BuildSystemPrompt_NoSessionFilesExist_ReturnsAgentDnaOnly()
    {
        // Session ?????? Session DNA ???? Agent DNA ??
        string prompt = _runner.BuildSystemPrompt(_testAgent, SessionId);

        prompt.Should().Contain("Soul");
    }

    // ?? BuildSystemPrompt?Session DNA ?? ??????????????????????????????????

    [Fact]
    public void BuildSystemPrompt_WithSessionDnaFilesInitialized_ContainsUserAndAgents()
    {
        _sessionDna.InitializeSession(SessionId);

        string prompt = _runner.BuildSystemPrompt(_testAgent, SessionId);

        prompt.Should().Contain("User");
        prompt.Should().Contain("Agents");
    }

    // ?? BuildSystemPrompt?Agent SOUL ????????????????????????????????????????

    [Fact]
    public void BuildSystemPrompt_WithCustomAgentSoul_IncludesSoulContent()
    {
        _agentDna.UpdateSoul(_testAgent.Id, "# Soul\n????????????");

        string prompt = _runner.BuildSystemPrompt(_testAgent, SessionId);

        prompt.Should().Contain("????????????");
    }

    [Fact]
    public void BuildSystemPrompt_WithAllDnaFiles_IncludesAllContent()
    {
        _agentDna.UpdateSoul(_testAgent.Id, "AGENT-SOUL-CONTENT");
        _agentDna.AppendMemory(_testAgent.Id, "AGENT-MEMORY-CONTENT");
        _sessionDna.InitializeSession(SessionId);
        _sessionDna.Update(SessionId, "USER.md", "USER-CONTENT");
        _sessionDna.Update(SessionId, "AGENTS.md", "AGENTS-CONTENT");

        string prompt = _runner.BuildSystemPrompt(_testAgent, SessionId);

        prompt.Should().Contain("AGENT-SOUL-CONTENT");
        prompt.Should().Contain("AGENT-MEMORY-CONTENT");
        prompt.Should().Contain("USER-CONTENT");
        prompt.Should().Contain("AGENTS-CONTENT");
    }

    // ?? BuildSystemPrompt???? ?????????????????????????????????????????????

    [Fact]
    public void BuildSystemPrompt_WithLongTermMemory_IncludesMemoryContent()
    {
        _memory.UpdateLongTermMemory(SessionId, "# Long Term Memory\n??????????");

        string prompt = _runner.BuildSystemPrompt(_testAgent, SessionId);

        prompt.Should().Contain("??????????");
    }

    [Fact]
    public void BuildSystemPrompt_WithRecentDailyMemory_IncludesFullDailyContent()
    {
        string today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        _memory.WriteDailyMemory(SessionId, today, "????? API ?????");

        string prompt = _runner.BuildSystemPrompt(_testAgent, SessionId);

        prompt.Should().Contain("????? API ?????");
    }

    // ?? BuildSystemPrompt?DNA + ???? ????????????????????????????????????

    [Fact]
    public void BuildSystemPrompt_WithDnaAndMemory_IncludesBoth()
    {
        _agentDna.UpdateSoul(_testAgent.Id, "# Soul\nDNA-MARKER");
        _memory.UpdateLongTermMemory(SessionId, "# Memory\nMEMORY-MARKER");

        string prompt = _runner.BuildSystemPrompt(_testAgent, SessionId);

        prompt.Should().Contain("DNA-MARKER");
        prompt.Should().Contain("MEMORY-MARKER");
    }

    [Fact]
    public void BuildSystemPrompt_AgentDnaAppearsBeforeSessionMemory_InCorrectOrder()
    {
        _agentDna.UpdateSoul(_testAgent.Id, "AGENT-DNA-SECTION");
        _memory.UpdateLongTermMemory(SessionId, "MEMORY-SECTION");

        string prompt = _runner.BuildSystemPrompt(_testAgent, SessionId);

        int dnaIndex = prompt.IndexOf("AGENT-DNA-SECTION", StringComparison.Ordinal);
        int memIndex = prompt.IndexOf("MEMORY-SECTION", StringComparison.Ordinal);

        dnaIndex.Should().BeLessThan(memIndex,
            "Agent DNA ?????? Session ?????????????Agent DNA ? Session DNA ? Memory?");
    }

    [Fact]
    public void BuildSystemPrompt_EmptySessionDnaFiles_StillContainsAgentDna()
    {
        // Session DNA ??????
        _sessionDna.InitializeSession(SessionId);
        _sessionDna.Update(SessionId, "USER.md", "");
        _sessionDna.Update(SessionId, "AGENTS.md", "  \n  ");

        // ????? Agent SOUL ??
        string prompt = _runner.BuildSystemPrompt(_testAgent, SessionId);

        // Agent ?? SOUL ??????
        prompt.Should().Contain("Soul");
    }

    // ?? ???? ??????????????????????????????????????????????????????????????

    private static ProviderClientFactory CreateNoOpClientFactory()
    {
        var mockProvider = Substitute.For<IModelProvider>();
        var mockClient = Substitute.For<IChatClient>();
        mockProvider.Supports(Arg.Any<ProviderProtocol>()).Returns(true);
        mockProvider.Create(Arg.Any<ProviderConfig>()).Returns(mockClient);
        return new ProviderClientFactory([mockProvider]);
    }
}
