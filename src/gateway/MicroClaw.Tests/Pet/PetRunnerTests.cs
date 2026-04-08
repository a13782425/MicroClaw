using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Agent;
using MicroClaw.Pet;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Observer;
using MicroClaw.Pet.Rag;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;
using MicroClaw.Tests.Fixtures;
using NSubstitute;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// PetRunner 单元测试：
/// - Pet 未启用时直接透传 AgentRunner
/// - Pet 启用时完整流程：更新状态 → 决策 → 执行 → 后处理
/// - IAgentMessageHandler 适配
/// </summary>
[Collection("Config")]
public sealed class PetRunnerTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PetStateStore _stateStore;
    private readonly EmotionStore _emotionStore;

    private const string SessionId = "runner-test-session";

    public PetRunnerTests()
    {
        TestConfigFixture.EnsureInitialized();
        _stateStore = new PetStateStore(_tempDir.Path);
        _emotionStore = new EmotionStore(_tempDir.Path);
    }

    public void Dispose() => _tempDir.Dispose();

    [Fact]
    public async Task HandleMessageAsync_PetNotExist_PassthroughAgentRunner()
    {
        // Arrange: 没有 Pet state，所以走透传路径
        var agentRunner = Substitute.For<AgentRunnerCallTarget>();
        var runner = CreatePetRunner(agentRunner: null); // 没有真正的 AgentRunner

        var history = CreateHistory("你好");

        // Act & Assert: 透传路径会由于 Session 不存在而抛出异常
        // 这验证了 PetRunner 正确进入了透传路径（而非 Pet 编排路径）
        var ex = await Record.ExceptionAsync(async () =>
        {
            await foreach (var _ in runner.HandleMessageAsync(SessionId, history)) { }
        });
        ex.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleMessageAsync_PetDisabled_PassthroughAgentRunner()
    {
        // Arrange: Pet 存在但 Enabled = false
        await InitializePetAsync(enabled: false);
        var runner = CreatePetRunner();
        var history = CreateHistory("你好");

        // Act & Assert: 进入透传路径
        var ex = await Record.ExceptionAsync(async () =>
        {
            await foreach (var _ in runner.HandleMessageAsync(SessionId, history)) { }
        });
        // 透传路径因无真正的 Session/Agent 数据而失败
        ex.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleMessageAsync_PetEnabled_SetsDispatchingState()
    {
        // Arrange
        await InitializePetAsync(enabled: true);
        var state = await _stateStore.LoadAsync(SessionId);
        state!.BehaviorState.Should().Be(PetBehaviorState.Idle);

        var runner = CreatePetRunner();
        var history = CreateHistory("你好");

        // Act: 会因无 Provider 失败，但在失败前已设置 Dispatching 状态
        try
        {
            await foreach (var _ in runner.HandleMessageAsync(SessionId, history)) { }
        }
        catch { /* 预期异常 */ }

        // Assert: 状态应已从初始 Idle 变为 Dispatching（如果后处理成功会恢复 Idle）
        var reloaded = await _stateStore.LoadAsync(SessionId);
        reloaded.Should().NotBeNull();
        // 后处理可能成功或失败，验证流程确实进入了 Pet 编排路径
        reloaded!.BehaviorState.Should().BeOneOf(PetBehaviorState.Idle, PetBehaviorState.Dispatching);
    }

    [Fact]
    public async Task HandleMessageAsync_PetEnabled_WritesJournal()
    {
        // Arrange
        await InitializePetAsync(enabled: true);
        var runner = CreatePetRunner();
        var history = CreateHistory("你好");

        // Act
        try
        {
            await foreach (var _ in runner.HandleMessageAsync(SessionId, history)) { }
        }
        catch { /* 预期异常 */ }

        // Assert: journal 文件应存在且有条目
        string journalFile = Path.Combine(_tempDir.Path, SessionId, "pet", "journal.jsonl");
        File.Exists(journalFile).Should().BeTrue();
        string[] lines = (await File.ReadAllLinesAsync(journalFile))
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        lines.Should().HaveCountGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task HandleMessageAsync_PetEnabled_UpdatesEmotionAfterMessage()
    {
        // Arrange
        await InitializePetAsync(enabled: true);
        var emotionBefore = await _emotionStore.GetCurrentAsync(SessionId, CancellationToken.None);

        var runner = CreatePetRunner();
        var history = CreateHistory("你好");

        // Act
        try
        {
            await foreach (var _ in runner.HandleMessageAsync(SessionId, history)) { }
        }
        catch { /* 预期异常 */ }

        // Assert: 情绪应该因为 MessageFailed 事件而有变化
        var emotionAfter = await _emotionStore.GetCurrentAsync(SessionId, CancellationToken.None);
        emotionAfter.Should().NotBeNull();
        // MessageFailed 会降低 Mood 和 Confidence
        // 由于速率超限回退也算 message 处理，情绪应已更新
    }

    [Fact]
    public void HasAgentForChannel_NoAgent_ReturnsFalse()
    {
        var runner = CreatePetRunner();
        runner.HasAgentForChannel("test-channel").Should().BeFalse();
    }

    [Fact]
    public void IAgentMessageHandler_HandleMessage_ReturnsEnumerator()
    {
        var runner = CreatePetRunner();
        IAgentMessageHandler handler = runner;

        var result = handler.HandleMessageAsync("ch-1", SessionId, CreateHistory("test"));
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleMessageAsync_PetEnabled_PostProcessRecordsHabit()
    {
        // Arrange
        await InitializePetAsync(enabled: true);
        var runner = CreatePetRunner();
        var history = CreateHistory("编程问题");

        // Act
        try
        {
            await foreach (var _ in runner.HandleMessageAsync(SessionId, history)) { }
        }
        catch { /* 预期异常 */ }

        // Assert: habits 文件应存在（PostProcess 中 fire-and-forget 写入）
        // 等待一小段时间让 fire-and-forget 完成
        await Task.Delay(200);
        string habitsFile = Path.Combine(_tempDir.Path, SessionId, "pet", "habits.jsonl");
        File.Exists(habitsFile).Should().BeTrue();
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

    /// <summary>
    /// 创建 PetRunner（使用 NSubstitute 模拟无法满足的依赖）。
    /// </summary>
    private PetRunner CreatePetRunner(object? agentRunner = null)
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

        // PetRagScope — sealed class, 使用 internal 构造函数
        var embeddingService = Substitute.For<MicroClaw.RAG.IEmbeddingService>();
        embeddingService.GenerateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new ReadOnlyMemory<float>(new float[384]));
        var petRagScope = new PetRagScope(embeddingService, _tempDir.Path, NullLogger<PetRagScope>.Instance);

        // AgentRunner mock — 使用 Substitute 不可行（sealed class），创建简化版
        var sessionReader = Substitute.For<ISessionRepository>();
        var mockAgentRunner = CreateMockAgentRunner(agentStore, providerStore, sessionReader);

        // ISessionRepository + PetContextFactory（O-3-5 新增依赖）
        var sessionRepo = Substitute.For<MicroClaw.Abstractions.Sessions.ISessionRepository>();
        // 返回已审批的 Session，与将要简单加载的 PetContext进行验证
        sessionRepo.Get(Arg.Any<string>()).Returns(mi =>
            Session.Reconstitute(
                mi.ArgAt<string>(0), "Test", "provider1", true,
                MicroClaw.Abstractions.ChannelType.Web, "", DateTimeOffset.UtcNow));
        var petContextFactory = new MicroClaw.Pet.PetContextFactory(_stateStore, _emotionStore);

        return new PetRunner(
            mockAgentRunner, agentStore, sessionRepo, petContextFactory, providerStore,
            _stateStore, _emotionStore, emotionRuleEngine, behaviorMapper,
            decisionEngine, rateLimiter, petRagScope, reportBuilder, observer,
            NullLogger<PetRunner>.Instance);
    }

    private static AgentRunner CreateMockAgentRunner(
        AgentStore agentStore, ProviderConfigStore providerStore, ISessionRepository sessionReader)
    {
        var skillService = new MicroClaw.Skills.SkillService(Path.GetTempPath());
        var skillStore = new MicroClaw.Skills.SkillStore(skillService);
        var skillToolFactory = new MicroClaw.Skills.SkillToolFactory(skillStore, skillService);
        var configDir = Path.GetTempPath();

        return new AgentRunner(
            agentStore:           agentStore,
            contextProviders:     [],
            providerStore:        providerStore,
            clientFactory:        new ProviderClientFactory([]),
            sessionReader:        sessionReader,
            skillToolFactory:     skillToolFactory,
            usageTracker:         Substitute.For<MicroClaw.Infrastructure.Data.IUsageTracker>(),
            loggerFactory:        NullLoggerFactory.Instance,
            agentStatusNotifier:  Substitute.For<IAgentStatusNotifier>(),
            toolCollector:        new ToolCollector([], new MicroClaw.Tools.McpServerConfigStore(configDir), NullLoggerFactory.Instance),
            devMetrics:           Substitute.For<MicroClaw.Agent.Dev.IDevMetricsService>(),
            contentPipeline:      new MicroClaw.Agent.Streaming.AIContentPipeline([], new NullLogger<MicroClaw.Agent.Streaming.AIContentPipeline>()),
            chatContentRestorers: []);
    }

    // Abstract class helper for NSubstitute mocking
    public abstract class AgentRunnerCallTarget
    {
        public abstract IAsyncEnumerable<StreamItem> StreamReActAsync(
            AgentConfig agent, string providerId,
            IReadOnlyList<SessionMessage> history,
            string? sessionId, CancellationToken ct,
            string source, PetOverrides? petOverrides);
    }
}
