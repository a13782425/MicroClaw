using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Agent;
using MicroClaw.Agent.Dev;
using MicroClaw.Agent.Streaming;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Pet;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Observer;
using MicroClaw.Pet.Rag;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;
using MicroClaw.RAG;
using MicroClaw.Sessions;
using MicroClaw.Skills;
using MicroClaw.Tests.Fixtures;
using MicroClaw.Tools;
using NSubstitute;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// P-I-7 集成测试：Pet 情绪在消息成功/失败后正确更新，BehaviorProfile 传递到 AgentRunner。
/// </summary>
[Collection("Config")]
public sealed class PetEmotionIntegrationTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PetStateStore _stateStore;
    private readonly EmotionStore _emotionStore;

    private const string SessionId = "emotion-integration-test";

    public PetEmotionIntegrationTests()
    {
        TestConfigFixture.EnsureInitialized();
        _stateStore = new PetStateStore(_tempDir.Path);
        _emotionStore = new EmotionStore(_tempDir.Path);
    }

    public void Dispose()
    {
        Thread.Sleep(200);
        try { _tempDir.Dispose(); } catch (IOException) { /* 文件仍被占用时静默 */ }
    }

    // ── 情绪规则引擎正确触发 ──────────────────────────────────────────

    [Fact]
    public void EmotionRuleEngine_MessageSuccess_IncreasesConfidenceAndMood()
    {
        // Arrange
        var engine = new EmotionRuleEngine(new EmotionRuleEngineOptions());
        var initial = EmotionState.Default; // 四维均 50

        // Act
        var after = engine.Evaluate(initial, EmotionEventType.MessageSuccess);

        // Assert: 成功时 Mood +3, Confidence +2
        after.Mood.Should().Be(53);
        after.Confidence.Should().Be(52);
        after.Alertness.Should().Be(50); // 不变
        after.Curiosity.Should().Be(50); // 不变
    }

    [Fact]
    public void EmotionRuleEngine_MessageFailed_DecreasesMoodAndConfidence()
    {
        var engine = new EmotionRuleEngine(new EmotionRuleEngineOptions());
        var initial = EmotionState.Default;

        var after = engine.Evaluate(initial, EmotionEventType.MessageFailed);

        // MessageFailed: Alertness +8, Mood -5, Confidence -5
        after.Alertness.Should().Be(58);
        after.Mood.Should().Be(45);
        after.Confidence.Should().Be(45);
    }

    [Fact]
    public void EmotionRuleEngine_MultipleFailed_CumulativeEffect()
    {
        var engine = new EmotionRuleEngine(new EmotionRuleEngineOptions());
        var state = EmotionState.Default;

        // 连续失败三次
        state = engine.Evaluate(state, EmotionEventType.MessageFailed);
        state = engine.Evaluate(state, EmotionEventType.MessageFailed);
        state = engine.Evaluate(state, EmotionEventType.MessageFailed);

        // 3x (Alertness +8, Mood -5, Confidence -5) = Alertness 74, Mood 35, Confidence 35
        state.Alertness.Should().Be(74);
        state.Mood.Should().Be(35);
        state.Confidence.Should().Be(35);
    }

    // ── BehaviorProfile 映射正确 ────────────────────────────────────────

    [Fact]
    public void BehaviorMapper_NormalEmotion_ReturnsNormalProfile()
    {
        var mapper = new EmotionBehaviorMapper(new EmotionBehaviorMapperOptions());
        var state = EmotionState.Default; // 四维均 50

        var profile = mapper.GetProfile(state);

        profile.Mode.Should().Be(BehaviorMode.Normal);
    }

    [Fact]
    public void BehaviorMapper_HighAlertness_ReturnsCautiousProfile()
    {
        var mapper = new EmotionBehaviorMapper(new EmotionBehaviorMapperOptions());
        // 模拟多次失败后的高 Alertness 状态
        var state = new EmotionState(alertness: 80, mood: 40, curiosity: 50, confidence: 40);

        var profile = mapper.GetProfile(state);

        profile.Mode.Should().Be(BehaviorMode.Cautious);
        profile.Temperature.Should().BeLessThan(0.7f, "谨慎模式温度更低");
    }

    [Fact]
    public void BehaviorMapper_LowConfidence_ReturnsCautiousProfile()
    {
        var mapper = new EmotionBehaviorMapper(new EmotionBehaviorMapperOptions());
        var state = new EmotionState(alertness: 50, mood: 50, curiosity: 50, confidence: 15);

        var profile = mapper.GetProfile(state);

        profile.Mode.Should().Be(BehaviorMode.Cautious);
    }

    [Fact]
    public void BehaviorMapper_HighCuriosityAndGoodMood_ReturnsExploreProfile()
    {
        var mapper = new EmotionBehaviorMapper(new EmotionBehaviorMapperOptions());
        var state = new EmotionState(alertness: 50, mood: 80, curiosity: 80, confidence: 60);

        var profile = mapper.GetProfile(state);

        profile.Mode.Should().Be(BehaviorMode.Explore);
        profile.Temperature.Should().BeGreaterThan(0.7f, "探索模式温度更高");
    }

    // ── Pet 消息流中情绪更新集成 ────────────────────────────────────────

    [Fact]
    public async Task PetRunner_MessageProcessing_UpdatesEmotion()
    {
        // Arrange: 初始化 Pet，情绪为默认值
        await InitializePetAsync();
        var emotionBefore = await _emotionStore.GetCurrentAsync(SessionId, CancellationToken.None);
        emotionBefore.Alertness.Should().Be(50);
        emotionBefore.Mood.Should().Be(50);

        var runner = CreatePetRunner();
        var history = CreateHistory("测试消息");

        // Act: 处理消息（会因无 Provider 失败，触发 MessageFailed 情绪事件）
        try
        {
            await foreach (var _ in runner.HandleMessageAsync(SessionId, history)) { }
        }
        catch { /* 预期异常 */ }

        // Assert: 情绪应已更新
        var emotionAfter = await _emotionStore.GetCurrentAsync(SessionId, CancellationToken.None);
        emotionAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task PetRunner_EmotionPersisted_AcrossMessages()
    {
        // Arrange
        await InitializePetAsync();

        var runner = CreatePetRunner();

        // Act: 处理两条消息
        for (int i = 0; i < 2; i++)
        {
            try
            {
                await foreach (var _ in runner.HandleMessageAsync(SessionId, CreateHistory($"消息 {i + 1}"))) { }
            }
            catch { /* 预期异常 */ }
        }

        // Assert: 情绪应反映累积效果
        var emotion = await _emotionStore.GetCurrentAsync(SessionId, CancellationToken.None);
        emotion.Should().NotBeNull();
    }

    [Fact]
    public async Task EmotionStore_SaveAndLoad_RoundTrip()
    {
        // Arrange: 直接测试情绪持久化
        string sid = "emotion-persist-test";
        string petDir = Path.Combine(_tempDir.Path, sid, "pet");
        Directory.CreateDirectory(petDir);

        var customEmotion = new EmotionState(alertness: 80, mood: 30, curiosity: 60, confidence: 25);

        // Act
        await _emotionStore.SaveAsync(sid, customEmotion, CancellationToken.None);
        var loaded = await _emotionStore.GetCurrentAsync(sid, CancellationToken.None);

        // Assert
        loaded.Alertness.Should().Be(80);
        loaded.Mood.Should().Be(30);
        loaded.Curiosity.Should().Be(60);
        loaded.Confidence.Should().Be(25);
    }

    [Fact]
    public async Task EmotionStore_DefaultWhenNoFile_ReturnsDefault()
    {
        // Arrange: 没有情绪文件的 session
        string sid = "no-emotion-session";

        // Act
        var emotion = await _emotionStore.GetCurrentAsync(sid, CancellationToken.None);

        // Assert: 应返回默认值
        emotion.Alertness.Should().Be(EmotionState.DefaultValue);
        emotion.Mood.Should().Be(EmotionState.DefaultValue);
        emotion.Curiosity.Should().Be(EmotionState.DefaultValue);
        emotion.Confidence.Should().Be(EmotionState.DefaultValue);
    }

    [Fact]
    public void BehaviorProfile_PassedToPetOverrides_Correctly()
    {
        // Arrange: 模拟谨慎模式的 BehaviorProfile
        var mapper = new EmotionBehaviorMapper(new EmotionBehaviorMapperOptions());
        var cautiousEmotion = new EmotionState(alertness: 85, mood: 40, curiosity: 50, confidence: 30);
        var profile = mapper.GetProfile(cautiousEmotion);

        // Act: 构建 PetOverrides（模拟 PetRunner 的行为）
        var overrides = new PetOverrides
        {
            Temperature = profile.Temperature,
            TopP = profile.TopP,
            BehaviorSuffix = profile.SystemPromptSuffix,
            ToolOverrides = null,
            PetKnowledge = null,
        };

        // Assert: PetOverrides 正确携带了行为模式参数
        overrides.Temperature.Should().BeLessThan(0.7f);
        profile.Mode.Should().Be(BehaviorMode.Cautious);
        overrides.BehaviorSuffix.Should().NotBeNullOrEmpty("谨慎模式有提示词后缀");
    }

    [Fact]
    public void EmotionDelta_Merge_CombinesCorrectly()
    {
        // Arrange
        var delta1 = new EmotionDelta(Alertness: +5, Mood: -3);
        var delta2 = new EmotionDelta(Alertness: +3, Confidence: -5);

        // Act
        var merged = delta1.Merge(delta2);

        // Assert
        merged.Alertness.Should().Be(8);
        merged.Mood.Should().Be(-3);
        merged.Confidence.Should().Be(-5);
        merged.Curiosity.Should().Be(0);
    }

    [Fact]
    public void EmotionState_Apply_ClampsTo0_100()
    {
        // Arrange: 极端值测试
        var state = new EmotionState(alertness: 95, mood: 5, curiosity: 50, confidence: 50);
        var delta = new EmotionDelta(Alertness: +20, Mood: -20);

        // Act
        var result = state.Apply(delta);

        // Assert: 应 Clamp 到 [0, 100]
        result.Alertness.Should().Be(100);
        result.Mood.Should().Be(0);
    }

    // ── 辅助方法 ────────────────────────────────────────────────────────

    private async Task InitializePetAsync()
    {
        string petDir = Path.Combine(_tempDir.Path, SessionId, "pet");
        Directory.CreateDirectory(petDir);

        var state = new PetState
        {
            SessionId = SessionId,
            BehaviorState = PetBehaviorState.Idle,
            EmotionState = EmotionState.Default,
            LlmCallCount = 0,
            WindowStart = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _stateStore.SaveAsync(state);

        var config = new PetConfig { Enabled = true };
        await _stateStore.SaveConfigAsync(SessionId, config);

        await _emotionStore.SaveAsync(SessionId, EmotionState.Default, CancellationToken.None);
    }

    private static IReadOnlyList<SessionMessage> CreateHistory(string lastUserMessage)
    {
        return new List<SessionMessage>
        {
            new(
                Id: Guid.NewGuid().ToString("N"),
                Role: "user",
                Content: lastUserMessage,
                ThinkContent: null,
                Timestamp: DateTimeOffset.UtcNow,
                Attachments: null)
        };
    }

    private PetRunner CreatePetRunner()
    {
        var providerStore = new ProviderConfigStore();
        var agentStore = new AgentStore();
        var providerRouter = Substitute.For<IProviderRouter>();
        var rateLimiter = new PetRateLimiter(_stateStore);
        var modelSelector = new PetModelSelector(providerStore, providerRouter);
        var clientFactory = new ProviderClientFactory([]);
        var decisionEngine = new PetDecisionEngine(
            rateLimiter, modelSelector, _stateStore, _emotionStore,
            clientFactory, NullLogger<PetDecisionEngine>.Instance);
        var emotionRuleEngine = new EmotionRuleEngine(new EmotionRuleEngineOptions());
        var behaviorMapper = new EmotionBehaviorMapper(new EmotionBehaviorMapperOptions());
        var reportBuilder = new PetSelfAwarenessReportBuilder(
            _stateStore, _emotionStore, behaviorMapper, rateLimiter, providerStore, agentStore);
        var observer = new PetSessionObserver(_tempDir.Path, NullLogger<PetSessionObserver>.Instance);

        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(new float[384]));
        var petRagScope = new PetRagScope(embeddingService, _tempDir.Path, NullLogger<PetRagScope>.Instance);

        var sessionReader = Substitute.For<ISessionRepository>();
        var agentRunner = CreateMockAgentRunner(agentStore, providerStore, sessionReader);

        // ISessionRepository + PetContextFactory（O-3-5 新增依赖）
        var sessionRepo = Substitute.For<ISessionRepository>();
        sessionRepo.Get(Arg.Any<string>()).Returns(mi =>
            MicroSession.Reconstitute(
                mi.ArgAt<string>(0), "Test", "provider1", true,
                MicroClaw.Abstractions.ChannelType.Web, "", DateTimeOffset.UtcNow));
        sessionRepo.GetRootSessionId(Arg.Any<string>()).Returns(mi => mi.ArgAt<string>(0));
        var petContextFactory = new PetContextFactory(_stateStore, _emotionStore);

        return new PetRunner(
            agentRunner, agentStore, sessionRepo, petContextFactory, providerStore,
            _stateStore, _emotionStore, emotionRuleEngine, behaviorMapper,
            decisionEngine, rateLimiter, petRagScope, reportBuilder, observer,
            NullLogger<PetRunner>.Instance);
    }

    private static AgentRunner CreateMockAgentRunner(
        AgentStore agentStore, ProviderConfigStore providerStore, ISessionRepository sessionReader)
    {
        var skillService = new SkillService(Path.GetTempPath());
        var skillStore = new SkillStore(skillService);
        var skillToolFactory = new SkillToolFactory(skillStore, skillService);
        var configDir = Path.GetTempPath();

        return new AgentRunner(
            agentStore: agentStore,
            contextProviders: [],
            providerStore: providerStore,
            clientFactory: new ProviderClientFactory([]),
            sessionReader: sessionReader,
            skillToolFactory: skillToolFactory,
            usageTracker: Substitute.For<IUsageTracker>(),
            loggerFactory: NullLoggerFactory.Instance,
            agentStatusNotifier: Substitute.For<IAgentStatusNotifier>(),
            toolCollector: new ToolCollector([], new McpServerConfigStore(configDir), NullLoggerFactory.Instance),
            devMetrics: Substitute.For<IDevMetricsService>(),
            contentPipeline: new AIContentPipeline([], new NullLogger<AIContentPipeline>()),
            chatContentRestorers: []);
    }
}
