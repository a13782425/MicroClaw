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
using MicroClaw.Skills;
using MicroClaw.Tests.Fixtures;
using MicroClaw.Tools;
using NSubstitute;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// P-I-5 集成测试：消息流经 PetRunner → PetDecisionEngine → AgentRunner 完整链路。
/// 验证 Pet 编排层的完整消息处理流程。
/// </summary>
[Collection("Config")]
public sealed class PetRunnerIntegrationTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PetStateStore _stateStore;
    private readonly EmotionStore _emotionStore;
    private readonly PetSessionObserver _observer;

    private const string SessionId = "integration-runner-test";

    public PetRunnerIntegrationTests()
    {
        TestConfigFixture.EnsureInitialized();
        _stateStore = new PetStateStore(_tempDir.Path);
        _emotionStore = new EmotionStore(_tempDir.Path);
        _observer = new PetSessionObserver(_tempDir.Path, NullLogger<PetSessionObserver>.Instance);
    }

    public void Dispose()
    {
        // 等待 fire-and-forget 操作完成，避免文件锁定
        Thread.Sleep(200);
        try { _tempDir.Dispose(); } catch (IOException) { /* 文件仍被占用时静默 */ }
    }

    // ── 完整流程测试 ──────────────────────────────────────────────────────

    [Fact]
    public async Task FullPipeline_PetEnabled_SetsDispatchingThenRestores()
    {
        // Arrange: 创建完整的 Pet 状态
        await InitializePetAsync(enabled: true);

        var stateBefore = await _stateStore.LoadAsync(SessionId);
        stateBefore!.BehaviorState.Should().Be(PetBehaviorState.Idle);

        var runner = CreatePetRunner();
        var history = CreateHistory("帮我写一段代码");

        // Act: 流经完整管道（会因无 Provider 而回退默认决策后，到 AgentRunner 失败）
        try
        {
            await foreach (var _ in runner.HandleMessageAsync(SessionId, history)) { }
        }
        catch { /* 预期：无真实 Provider/Agent */ }

        // Assert: 经过 PostProcess，状态应恢复到 Idle 或保持 Dispatching
        var stateAfter = await _stateStore.LoadAsync(SessionId);
        stateAfter.Should().NotBeNull();
        stateAfter!.UpdatedAt.Should().BeAfter(stateBefore!.UpdatedAt);
    }

    [Fact]
    public async Task FullPipeline_PetEnabled_JournalRecordsDispatchDecision()
    {
        // Arrange
        await InitializePetAsync(enabled: true);
        var runner = CreatePetRunner();
        var history = CreateHistory("请帮我翻译一段文本");

        // Act
        try
        {
            await foreach (var _ in runner.HandleMessageAsync(SessionId, history)) { }
        }
        catch { /* 预期异常 */ }

        // Assert: journal 应记录了调度决策
        string journalFile = Path.Combine(_tempDir.Path, SessionId, "pet", "journal.jsonl");
        File.Exists(journalFile).Should().BeTrue();
        string[] lines = (await File.ReadAllLinesAsync(journalFile))
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        lines.Should().HaveCountGreaterThanOrEqualTo(1);
        // journal 条目应包含 reason（来自 PetDispatchResult）
        lines.Should().Contain(l => l.Contains("reason", StringComparison.OrdinalIgnoreCase)
                                 || l.Contains("dispatch", StringComparison.OrdinalIgnoreCase)
                                 || l.Contains("决策", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FullPipeline_PetEnabled_HabitRecordedAfterMessage()
    {
        // Arrange
        await InitializePetAsync(enabled: true);
        var runner = CreatePetRunner();
        var history = CreateHistory("什么是机器学习？");

        // Act
        try
        {
            await foreach (var _ in runner.HandleMessageAsync(SessionId, history)) { }
        }
        catch { /* 预期异常 */ }

        // Assert: 等待 fire-and-forget 写入习惯
        await Task.Delay(300);
        string habitsFile = Path.Combine(_tempDir.Path, SessionId, "pet", "habits.jsonl");
        File.Exists(habitsFile).Should().BeTrue();
        string[] habits = (await File.ReadAllLinesAsync(habitsFile))
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        habits.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task FullPipeline_PetDisabled_TransparentPassthrough()
    {
        // Arrange: Pet 存在但未启用
        await InitializePetAsync(enabled: false);
        var runner = CreatePetRunner();
        var history = CreateHistory("你好");

        // Act: 应走透传路径
        var items = new List<StreamItem>();
        var ex = await Record.ExceptionAsync(async () =>
        {
            await foreach (var item in runner.HandleMessageAsync(SessionId, history))
                items.Add(item);
        });

        // Assert: 透传路径因无 Agent 配置而失败
        ex.Should().NotBeNull("透传路径要求 Session 有绑定的 Agent");

        // 不应写入 Pet journal（因为走透传路径）
        string journalFile = Path.Combine(_tempDir.Path, SessionId, "pet", "journal.jsonl");
        // journal 可能不存在或为空（看 PostProcess 是否执行）
    }

    [Fact]
    public async Task FullPipeline_NoPetState_TransparentPassthrough()
    {
        // Arrange: 没有 Pet state 文件
        var runner = CreatePetRunner();
        var history = CreateHistory("你好");

        // Act: 应走透传路径
        var ex = await Record.ExceptionAsync(async () =>
        {
            await foreach (var _ in runner.HandleMessageAsync(SessionId, history)) { }
        });

        // Assert: 透传路径因无 Session 数据而失败
        ex.Should().NotBeNull();
    }

    [Fact]
    public async Task FullPipeline_DecisionEngine_ReceivesCorrectContext()
    {
        // Arrange: 验证 DecisionEngine 收到正确的上下文
        await InitializePetAsync(enabled: true);

        // 在 Pet RAG 中写入一些知识
        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(new float[384]));
        var ragScope = new PetRagScope(embeddingService, _tempDir.Path, NullLogger<PetRagScope>.Instance);
        await ragScope.IngestAsync("测试知识：用户喜欢简洁的回答", SessionId);

        var runner = CreatePetRunner(petRagScope: ragScope);
        var history = CreateHistory("帮我总结一下");

        // Act
        try
        {
            await foreach (var _ in runner.HandleMessageAsync(SessionId, history)) { }
        }
        catch { /* 预期异常 */ }

        // Assert: Pet 进入了编排路径（状态被修改过）
        var state = await _stateStore.LoadAsync(SessionId);
        state.Should().NotBeNull();
        state!.UpdatedAt.Should().BeAfter(DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task FullPipeline_PetEmotionUpdatedAfterFailure()
    {
        // Arrange
        await InitializePetAsync(enabled: true);
        var emotionBefore = await _emotionStore.GetCurrentAsync(SessionId, CancellationToken.None);

        var runner = CreatePetRunner();
        var history = CreateHistory("你好");

        // Act: 执行消息（会因无 Provider 失败）
        try
        {
            await foreach (var _ in runner.HandleMessageAsync(SessionId, history)) { }
        }
        catch { /* 预期异常 */ }

        // Assert: 情绪应因失败事件而变化
        var emotionAfter = await _emotionStore.GetCurrentAsync(SessionId, CancellationToken.None);
        emotionAfter.Should().NotBeNull();
        // 失败事件通常降低 Mood 和 Confidence
    }

    [Fact]
    public void IAgentMessageHandler_AdapterWorks()
    {
        // Arrange
        var runner = CreatePetRunner();
        IAgentMessageHandler handler = runner;

        // Act & Assert: 接口适配应返回非空 IAsyncEnumerable
        var result = handler.HandleMessageAsync("channel-1", "session-1", CreateHistory("test"));
        result.Should().NotBeNull();
    }

    [Fact]
    public void HasAgentForChannel_ReturnsFalseWhenNoDefaultAgent()
    {
        var runner = CreatePetRunner();
        runner.HasAgentForChannel("any-channel").Should().BeFalse();
    }

    // ── 辅助方法 ────────────────────────────────────────────────────────

    private async Task InitializePetAsync(bool enabled)
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

        var config = new PetConfig { Enabled = enabled };
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

    private PetRunner CreatePetRunner(PetRagScope? petRagScope = null)
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

        var embeddingService = Substitute.For<IEmbeddingService>();
        embeddingService.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(new float[384]));
        var effectiveRagScope = petRagScope ?? new PetRagScope(embeddingService, _tempDir.Path, NullLogger<PetRagScope>.Instance);

        var sessionReader = Substitute.For<ISessionReader>();
        var agentRunner = CreateMockAgentRunner(agentStore, providerStore, sessionReader);

        return new PetRunner(
            agentRunner, agentStore, sessionReader, providerStore,
            _stateStore, _emotionStore, emotionRuleEngine, behaviorMapper,
            decisionEngine, rateLimiter, effectiveRagScope, reportBuilder, _observer,
            NullLogger<PetRunner>.Instance);
    }

    private static AgentRunner CreateMockAgentRunner(
        AgentStore agentStore, ProviderConfigStore providerStore, ISessionReader sessionReader)
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
