using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Agent.ContextProviders;
using AgentEntity = MicroClaw.Agent.Agent;
using MicroClaw.Agent.Dev;
using MicroClaw.Agent.Memory;
using MicroClaw.Channels;
using MicroClaw.Pet.Emotion;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers;
using MicroClaw.Configuration;
using MicroClaw.Skills;
using MicroClaw.Tests.Fixtures;
using MicroClaw.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace MicroClaw.Tests.Agents;

/// <summary>
/// 验证 AgentRunner 情绪系统集成：
/// - BuildSystemPromptAsync 追加行为模式后缀
/// - 情绪服务为 null 时降级正常运行（无异常）
/// - 有情绪服务时 GetCurrentAsync 被调用并在执行后 SaveAsync 被调用
/// </summary>
[Collection("Config")]
public sealed class AgentRunnerEmotionTests : IDisposable
{
    private const string AgentId = "emotion-test-agent";
    private const string SessionId = "emotion-test-session";

    private readonly TempDirectoryFixture _tempDir = new();
    private readonly AgentDnaService _agentDna;
    private readonly AgentEntity _testAgent;

    public AgentRunnerEmotionTests()
    {
        TestConfigFixture.EnsureInitialized();
        string agentsDir = Path.Combine(_tempDir.Path, "agents");
        _agentDna = new AgentDnaService(agentsDir);

        _testAgent = AgentEntity.Reconstitute(
            id: AgentId,
            name: "Emotion Test Agent",
            description: "",
            isEnabled: true,
            disabledSkillIds: [],
            disabledMcpServerIds: [],
            toolGroupConfigs: [],
            createdAtUtc: DateTimeOffset.UtcNow);
        _agentDna.InitializeAgent(_testAgent.Id);
    }

    public void Dispose()
    {
        _tempDir.Dispose();
    }

    // ── 辅助方法 ─────────────────────────────────────────────────────────────

    private AgentRunner BuildRunner(
        IAgentContextProvider[]? providers = null)
    {
        string configDir = _tempDir.Path;
        var agentStore = new AgentStore();
        var skillService = new SkillService(_tempDir.Path);
        var skillStore = new SkillStore(skillService);
        var skillToolFactory = new SkillToolFactory(skillStore, skillService);
        var skillInvocationTool = new SkillInvocationTool(
            skillToolFactory,
            skillService,
            NullLoggerFactory.Instance.CreateLogger<SkillInvocationTool>(),
            subAgentRunner: null);

        return new AgentRunner(
            agentStore:            agentStore,
            contextProviders:      providers ?? [new AgentDnaContextProvider(_agentDna)],
            providerStore:         new ProviderConfigStore(),
            clientFactory:         CreateNoOpClientFactory(),
            sessionReader:         Substitute.For<ISessionRepository>(),
            skillToolFactory:      skillToolFactory,
            usageTracker:          Substitute.For<IUsageTracker>(),
            loggerFactory:         NullLoggerFactory.Instance,
            agentStatusNotifier:   Substitute.For<IAgentStatusNotifier>(),
            toolCollector:         new ToolCollector([], new McpServerConfigStore(configDir), NullLoggerFactory.Instance),
            devMetrics:            Substitute.For<IDevMetricsService>(),
            contentPipeline:       new MicroClaw.Agent.Streaming.AIContentPipeline([], NullLoggerFactory.Instance.CreateLogger<MicroClaw.Agent.Streaming.AIContentPipeline>()),
            chatContentRestorers:  []);
    }

    private static ProviderClientFactory CreateNoOpClientFactory()
    {
        var mockProvider = Substitute.For<IModelProvider>();
        var mockClient = Substitute.For<IChatClient>();
        mockProvider.Supports(Arg.Any<ProviderProtocol>()).Returns(true);
        mockProvider.Create(Arg.Any<ProviderConfig>()).Returns(mockClient);
        return new ProviderClientFactory([mockProvider]);
    }

    // ── BuildSystemPromptAsync 行为后缀测试 ───────────────────────────────────

    [Fact]
    public async Task BuildSystemPromptAsync_WithBehaviorSuffix_AppendsSuffixAtEnd()
    {
        // Arrange
        var runner = BuildRunner();
        const string behaviorSuffix = "请谨慎行事，仔细验证每一步。";

        // Act
        string prompt = await runner.BuildSystemPromptAsync(
            _testAgent, sessionId: null, behaviorSuffix: behaviorSuffix);

        // Assert
        prompt.Should().EndWith(behaviorSuffix);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithSkillContextAndBehaviorSuffix_BehaviorSuffixAfterSkillContext()
    {
        // Arrange
        var runner = BuildRunner();
        const string skillContext = "## 技能目录\n- skill_a";
        const string behaviorSuffix = "请大胆探索。";

        // Act
        string prompt = await runner.BuildSystemPromptAsync(
            _testAgent, sessionId: null, skillContext: skillContext, behaviorSuffix: behaviorSuffix);

        // Assert
        int skillIdx = prompt.IndexOf(skillContext, StringComparison.Ordinal);
        int suffixIdx = prompt.IndexOf(behaviorSuffix, StringComparison.Ordinal);
        skillIdx.Should().BeGreaterThan(0);
        suffixIdx.Should().BeGreaterThan(skillIdx,
            "行为模式后缀应排在 Skill Context 之后");
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithNullBehaviorSuffix_DoesNotAppendExtra()
    {
        // Arrange
        var runner = BuildRunner();

        // Act
        string withoutSuffix = await runner.BuildSystemPromptAsync(_testAgent, sessionId: null, behaviorSuffix: null);
        string withSuffix = await runner.BuildSystemPromptAsync(_testAgent, sessionId: null, behaviorSuffix: "提示语");

        // Assert
        withSuffix.Length.Should().BeGreaterThan(withoutSuffix.Length);
        withoutSuffix.Should().NotContain("提示语");
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithEmptyBehaviorSuffix_DoesNotAppendEmptyPart()
    {
        // Arrange
        var runner = BuildRunner();

        // Act
        string withEmpty = await runner.BuildSystemPromptAsync(
            _testAgent, sessionId: null, behaviorSuffix: "  ");
        string withoutSuffix = await runner.BuildSystemPromptAsync(
            _testAgent, sessionId: null, behaviorSuffix: null);

        // Assert: 空白后缀不应产生额外内容
        withEmpty.Should().Be(withoutSuffix);
    }

    // 情绪服务注入与调用验证 ──────────────────────────────

    // Note: AgentRunner 已不再直接依赖情绪服务，该验证已移至 Pet 层

    [Fact]
    public async Task BuildSystemPromptAsync_WithMappedCautiousProfile_AppendsCautiousSuffix()
    {
        // Arrange: 构造 Cautious 情绪状态（高警觉）并验证 suffix 正确注入
        var cautiousState = new EmotionState(alertness: 85, mood: 50, curiosity: 50, confidence: 50);
        var mapper = new EmotionBehaviorMapper();
        BehaviorProfile profile = mapper.GetProfile(cautiousState);
        profile.Mode.Should().Be(BehaviorMode.Cautious);

        var runner = BuildRunner();

        // Act
        string prompt = await runner.BuildSystemPromptAsync(
            _testAgent, sessionId: null, behaviorSuffix: profile.SystemPromptSuffix);

        // Assert
        prompt.Should().Contain(profile.SystemPromptSuffix);
        prompt.Should().EndWith(profile.SystemPromptSuffix);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithMappedExploreProfile_AppendsExploreSuffix()
    {
        // Arrange: 构造 Explore 情绪状态（高好奇 + 高心情）
        var exploreState = new EmotionState(alertness: 40, mood: 80, curiosity: 80, confidence: 50);
        var mapper = new EmotionBehaviorMapper();
        BehaviorProfile profile = mapper.GetProfile(exploreState);
        profile.Mode.Should().Be(BehaviorMode.Explore);

        var runner = BuildRunner();

        // Act
        string prompt = await runner.BuildSystemPromptAsync(
            _testAgent, sessionId: null, behaviorSuffix: profile.SystemPromptSuffix);

        // Assert
        prompt.Should().Contain(profile.SystemPromptSuffix);
    }

    [Fact]
    public async Task BuildSystemPromptAsync_WithNormalProfile_NoSuffixAdded()
    {
        // Arrange: 默认情绪状态 → Normal 模式，SystemPromptSuffix 为空字符串
        var normalState = EmotionState.Default;
        var mapper = new EmotionBehaviorMapper();
        BehaviorProfile profile = mapper.GetProfile(normalState);
        profile.Mode.Should().Be(BehaviorMode.Normal);
        profile.SystemPromptSuffix.Should().BeEmpty();

        var runner = BuildRunner();

        // Act: 传入空字符串后缀
        string withEmpty = await runner.BuildSystemPromptAsync(
            _testAgent, sessionId: null, behaviorSuffix: profile.SystemPromptSuffix);
        string withNull = await runner.BuildSystemPromptAsync(
            _testAgent, sessionId: null, behaviorSuffix: null);

        // Assert: 空后缀与 null 后缀结果一致
        withEmpty.Should().Be(withNull);
    }

    [Fact]
    public async Task EmotionStoreAndRuleEngine_Integration_StateUpdatesAfterEvent()
    {
        // Arrange: 使用文件系统 EmotionStore（Pet 版本，per-session）
        string dir = Path.Combine(Path.GetTempPath(), "mc_pet_emotion_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var store = new EmotionStore(dir);
            var engine = new EmotionRuleEngine();

            // 初始状态
            EmotionState initial = await store.GetCurrentAsync(SessionId);
            initial.Should().Be(EmotionState.Default);

            // 模拟任务完成
            EmotionState afterComplete = engine.Evaluate(initial, EmotionEventType.TaskCompleted);
            await store.SaveAsync(SessionId, afterComplete);

            // 验证持久化
            EmotionState persisted = await store.GetCurrentAsync(SessionId);
            persisted.Mood.Should().BeGreaterThan(initial.Mood, "任务完成后心情应提升");
            persisted.Confidence.Should().BeGreaterThan(initial.Confidence, "任务完成后信心应提升");
            persisted.Alertness.Should().BeLessThan(initial.Alertness, "任务完成后警觉度应降低");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 忽略 清理错误 */ }
        }
    }
}
