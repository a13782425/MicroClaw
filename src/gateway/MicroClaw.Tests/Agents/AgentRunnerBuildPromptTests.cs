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
/// 验证 M-04/M-05 重构后的 AgentRunner.BuildSystemPrompt：
/// System Prompt 由 Session DNA（SOUL/USER/AGENTS）+ Session 记忆（长期+每日权重）构建，
/// 不再依赖旧 DNAService。
/// </summary>
public sealed class AgentRunnerBuildPromptTests : IDisposable
{
    private const string SessionId = "test-session-001";

    private readonly DatabaseFixture _db = new();
    private readonly TempDirectoryFixture _tempDir = new();

    private readonly SessionDnaService _sessionDna;
    private readonly MemoryService _memory;
    private readonly AgentRunner _runner;

    public AgentRunnerBuildPromptTests()
    {
        _sessionDna = new SessionDnaService(_tempDir.Path);
        _memory = new MemoryService(_tempDir.Path);

        IDbContextFactory<GatewayDbContext> dbFactory = _db.CreateFactory();

        var skillService = new SkillService(_tempDir.Path);
        var skillRunner = new SkillRunner(skillService,
            NullLogger<SkillRunner>.Instance);
        var skillStore = new SkillStore(dbFactory);
        var skillToolFactory = new SkillToolFactory(
            skillStore, skillService, skillRunner,
            _tempDir.Path, NullLoggerFactory.Instance);

        _runner = new AgentRunner(
            agentStore:            new AgentStore(dbFactory),
            sessionDnaService:     _sessionDna,
            memoryService:         _memory,
            mcpServerConfigStore:  new McpServerConfigStore(dbFactory),
            providerStore:         new ProviderConfigStore(dbFactory),
            clientFactory:         CreateNoOpClientFactory(),
            sessionReader:         Substitute.For<ISessionReader>(),
            cronJobStore:          new CronJobStore(dbFactory),
            cronScheduler:         Substitute.For<ICronJobScheduler>(),
            skillToolFactory:      skillToolFactory,
            subAgentRunner:        Substitute.For<ISubAgentRunner>(),
            usageTracker:          Substitute.For<IUsageTracker>(),
            loggerFactory:         NullLoggerFactory.Instance,
            agentStatusNotifier:   Substitute.For<IAgentStatusNotifier>(),
            channelConfigStore:    new ChannelConfigStore(dbFactory),
            toolProviders:         []);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
        _db.Dispose();
    }

    // ── BuildSystemPrompt：无 sessionId ──────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_NullSessionId_ReturnsEmpty()
    {
        _runner.BuildSystemPrompt(null).Should().BeEmpty();
    }

    [Fact]
    public void BuildSystemPrompt_EmptySessionId_ReturnsEmpty()
    {
        _runner.BuildSystemPrompt(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void BuildSystemPrompt_WhitespaceSessionId_ReturnsEmpty()
    {
        _runner.BuildSystemPrompt("   ").Should().BeEmpty();
    }

    // ── BuildSystemPrompt：无 DNA 文件 ────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_NoFilesExist_ReturnsEmpty()
    {
        // Session 未初始化，无任何文件
        _runner.BuildSystemPrompt(SessionId).Should().BeEmpty();
    }

    // ── BuildSystemPrompt：仅 DNA 文件 ────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_WithDnaFilesInitialized_ContainsDefaultTemplateContent()
    {
        _sessionDna.InitializeSession(SessionId);

        string prompt = _runner.BuildSystemPrompt(SessionId);

        // 默认模板至少包含 SOUL/USER/AGENTS 的 Markdown 标题标记
        prompt.Should().Contain("Soul");
        prompt.Should().Contain("User");
        prompt.Should().Contain("Agents");
    }

    [Fact]
    public void BuildSystemPrompt_WithCustomSoulContent_IncludesSoulContent()
    {
        _sessionDna.InitializeSession(SessionId);
        _sessionDna.Update(SessionId, "SOUL.md", "# Soul\n你是一个专业的技术助手。");

        string prompt = _runner.BuildSystemPrompt(SessionId);

        prompt.Should().Contain("你是一个专业的技术助手。");
    }

    [Fact]
    public void BuildSystemPrompt_WithAllThreeDnaFiles_IncludesAllContent()
    {
        _sessionDna.InitializeSession(SessionId);
        _sessionDna.Update(SessionId, "SOUL.md", "SOUL-CONTENT");
        _sessionDna.Update(SessionId, "USER.md", "USER-CONTENT");
        _sessionDna.Update(SessionId, "AGENTS.md", "AGENTS-CONTENT");

        string prompt = _runner.BuildSystemPrompt(SessionId);

        prompt.Should().Contain("SOUL-CONTENT");
        prompt.Should().Contain("USER-CONTENT");
        prompt.Should().Contain("AGENTS-CONTENT");
    }

    // ── BuildSystemPrompt：仅记忆 ─────────────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_WithLongTermMemory_IncludesMemoryContent()
    {
        _memory.UpdateLongTermMemory(SessionId, "# Long Term Memory\n用户喜欢简洁的回答。");

        string prompt = _runner.BuildSystemPrompt(SessionId);

        prompt.Should().Contain("用户喜欢简洁的回答。");
    }

    [Fact]
    public void BuildSystemPrompt_WithRecentDailyMemory_IncludesFullDailyContent()
    {
        string today = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd");
        _memory.WriteDailyMemory(SessionId, today, "今日讨论了 API 设计方案。");

        string prompt = _runner.BuildSystemPrompt(SessionId);

        prompt.Should().Contain("今日讨论了 API 设计方案。");
    }

    // ── BuildSystemPrompt：DNA + 记忆组合 ────────────────────────────────────

    [Fact]
    public void BuildSystemPrompt_WithDnaAndMemory_IncludesBoth()
    {
        _sessionDna.InitializeSession(SessionId);
        _sessionDna.Update(SessionId, "SOUL.md", "# Soul\nDNA-MARKER");
        _memory.UpdateLongTermMemory(SessionId, "# Memory\nMEMORY-MARKER");

        string prompt = _runner.BuildSystemPrompt(SessionId);

        prompt.Should().Contain("DNA-MARKER");
        prompt.Should().Contain("MEMORY-MARKER");
    }

    [Fact]
    public void BuildSystemPrompt_DnaAppearsBeforeMemory_InCorrectOrder()
    {
        _sessionDna.InitializeSession(SessionId);
        _sessionDna.Update(SessionId, "SOUL.md", "DNA-SECTION");
        _memory.UpdateLongTermMemory(SessionId, "MEMORY-SECTION");

        string prompt = _runner.BuildSystemPrompt(SessionId);

        int dnaIndex = prompt.IndexOf("DNA-SECTION", StringComparison.Ordinal);
        int memIndex = prompt.IndexOf("MEMORY-SECTION", StringComparison.Ordinal);

        dnaIndex.Should().BeLessThan(memIndex,
            "DNA 内容应出现在记忆内容之前（按构建顺序：DNA → Memory）");
    }

    [Fact]
    public void BuildSystemPrompt_EmptyDnaFiles_SkipsEmptyFiles()
    {
        // 初始化后清空所有文件内容
        _sessionDna.InitializeSession(SessionId);
        _sessionDna.Update(SessionId, "SOUL.md", "   ");
        _sessionDna.Update(SessionId, "USER.md", "");
        _sessionDna.Update(SessionId, "AGENTS.md", "  \n  ");

        // 无记忆
        string prompt = _runner.BuildSystemPrompt(SessionId);

        // 所有 DNA 文件全为空白，且无记忆，整体应为空
        prompt.Should().BeEmpty();
    }

    // ── 辅助方法 ──────────────────────────────────────────────────────────────

    private static ProviderClientFactory CreateNoOpClientFactory()
    {
        var mockProvider = Substitute.For<IModelProvider>();
        var mockClient = Substitute.For<IChatClient>();
        mockProvider.Supports(Arg.Any<ProviderProtocol>()).Returns(true);
        mockProvider.Create(Arg.Any<ProviderConfig>()).Returns(mockClient);
        return new ProviderClientFactory([mockProvider]);
    }
}
