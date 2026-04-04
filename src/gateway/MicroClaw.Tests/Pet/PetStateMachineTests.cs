using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using MicroClaw.Pet;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.StateMachine.States;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// PetStateMachine 单元测试：
/// - ParseDecision：各种 JSON 输入的容错解析
/// - ExtractJson：Markdown 代码块提取
/// - EvaluateAsync：速率超限 / 无 Provider / 正常 LLM 调用
/// - PetStateMachinePrompt：System/User Prompt 构建
/// </summary>
[Collection("Config")]
public sealed class PetStateMachineTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PetStateStore _stateStore;
    private readonly EmotionStore _emotionStore;

    private const string SessionId = "statemachine-test-session";

    public PetStateMachineTests()
    {
        _stateStore = new PetStateStore(_tempDir.Path);
        _emotionStore = new EmotionStore(_tempDir.Path);
    }

    public void Dispose() => _tempDir.Dispose();

    #region ParseDecision Tests

    [Fact]
    public void ParseDecision_ValidJson_ReturnsCorrectDecision()
    {
        const string json = """
            {
              "newState": "Learning",
              "emotionShift": { "alertness": 5, "mood": 3, "curiosity": 10, "confidence": 0 },
              "reason": "发现新内容值得学习",
              "plannedActions": [
                { "type": "FetchWeb", "parameter": "https://example.com", "reason": "获取新知识" }
              ]
            }
            """;

        var decision = PetStateMachine.ParseDecision(json);

        decision.NewState.Should().Be(PetBehaviorState.Learning);
        decision.EmotionShift.Alertness.Should().Be(5);
        decision.EmotionShift.Mood.Should().Be(3);
        decision.EmotionShift.Curiosity.Should().Be(10);
        decision.EmotionShift.Confidence.Should().Be(0);
        decision.Reason.Should().Be("发现新内容值得学习");
        decision.PlannedActions.Should().HaveCount(1);
        decision.PlannedActions[0].Type.Should().Be(PetActionType.FetchWeb);
        decision.PlannedActions[0].Parameter.Should().Be("https://example.com");
    }

    [Fact]
    public void ParseDecision_WrappedInMarkdownCodeBlock_ExtractsCorrectly()
    {
        const string text = """
            ```json
            {
              "newState": "Idle",
              "emotionShift": { "alertness": 0, "mood": 0, "curiosity": 0, "confidence": 0 },
              "reason": "无特殊情况",
              "plannedActions": []
            }
            ```
            """;

        var decision = PetStateMachine.ParseDecision(text);

        decision.NewState.Should().Be(PetBehaviorState.Idle);
        decision.Reason.Should().Be("无特殊情况");
        decision.PlannedActions.Should().BeEmpty();
    }

    [Fact]
    public void ParseDecision_EmptyResponse_ReturnsFallback()
    {
        var decision = PetStateMachine.ParseDecision("");

        decision.NewState.Should().Be(PetBehaviorState.Idle);
        decision.Reason.Should().Contain("回退");
    }

    [Fact]
    public void ParseDecision_InvalidJson_ReturnsFallback()
    {
        var decision = PetStateMachine.ParseDecision("this is not json at all");

        decision.NewState.Should().Be(PetBehaviorState.Idle);
        decision.Reason.Should().Contain("回退");
    }

    [Fact]
    public void ParseDecision_MissingFields_UsesDefaults()
    {
        const string json = """{ "newState": "Reflecting", "reason": "思考中" }""";

        var decision = PetStateMachine.ParseDecision(json);

        decision.NewState.Should().Be(PetBehaviorState.Reflecting);
        decision.EmotionShift.Should().Be(EmotionDelta.Zero);
        decision.PlannedActions.Should().BeEmpty();
    }

    [Fact]
    public void ParseDecision_UnknownState_DefaultsToIdle()
    {
        const string json = """{ "newState": "UnknownState", "reason": "test" }""";

        var decision = PetStateMachine.ParseDecision(json);

        decision.NewState.Should().Be(PetBehaviorState.Idle);
    }

    [Fact]
    public void ParseDecision_UnknownActionType_Skipped()
    {
        const string json = """
            {
              "newState": "Idle",
              "reason": "test",
              "plannedActions": [
                { "type": "UnknownAction", "reason": "skip me" },
                { "type": "NotifyUser", "parameter": "hello", "reason": "valid" }
              ]
            }
            """;

        var decision = PetStateMachine.ParseDecision(json);

        decision.PlannedActions.Should().HaveCount(1);
        decision.PlannedActions[0].Type.Should().Be(PetActionType.NotifyUser);
    }

    [Fact]
    public void ParseDecision_MultipleActions_AllParsed()
    {
        const string json = """
            {
              "newState": "Learning",
              "emotionShift": { "alertness": 5, "mood": 0, "curiosity": 10, "confidence": 2 },
              "reason": "多项活动",
              "plannedActions": [
                { "type": "FetchWeb", "parameter": "https://example.com" },
                { "type": "SummarizeToMemory", "reason": "摘要保存" },
                { "type": "EvolvePrompts" }
              ]
            }
            """;

        var decision = PetStateMachine.ParseDecision(json);

        decision.PlannedActions.Should().HaveCount(3);
        decision.PlannedActions[0].Type.Should().Be(PetActionType.FetchWeb);
        decision.PlannedActions[1].Type.Should().Be(PetActionType.SummarizeToMemory);
        decision.PlannedActions[2].Type.Should().Be(PetActionType.EvolvePrompts);
    }

    [Fact]
    public void ParseDecision_CaseInsensitiveState()
    {
        const string json = """{ "newState": "resting", "reason": "lower case" }""";

        var decision = PetStateMachine.ParseDecision(json);

        decision.NewState.Should().Be(PetBehaviorState.Resting);
    }

    [Fact]
    public void ParseDecision_NegativeEmotionShift()
    {
        const string json = """
            {
              "newState": "Panic",
              "emotionShift": { "alertness": 15, "mood": -10, "curiosity": -5, "confidence": -15 },
              "reason": "系统异常"
            }
            """;

        var decision = PetStateMachine.ParseDecision(json);

        decision.EmotionShift.Alertness.Should().Be(15);
        decision.EmotionShift.Mood.Should().Be(-10);
        decision.EmotionShift.Curiosity.Should().Be(-5);
        decision.EmotionShift.Confidence.Should().Be(-15);
    }

    #endregion

    #region ExtractJson Tests

    [Fact]
    public void ExtractJson_PlainJson_ReturnsAsIs()
    {
        const string input = """{"key": "value"}""";
        PetStateMachine.ExtractJson(input).Should().Be(input);
    }

    [Fact]
    public void ExtractJson_MarkdownWrapped_ExtractsJson()
    {
        const string input = """
            ```json
            {"key": "value"}
            ```
            """;

        var result = PetStateMachine.ExtractJson(input);
        result.Should().Contain("\"key\"");
        result.Should().StartWith("{");
        result.Should().EndWith("}");
    }

    [Fact]
    public void ExtractJson_MarkdownWithSurroundingText_ExtractsJson()
    {
        const string input = """
            Here is the result:
            ```
            {"newState": "Idle"}
            ```
            That's my decision.
            """;

        var result = PetStateMachine.ExtractJson(input);
        result.Should().Contain("newState");
    }

    #endregion

    #region PetStateMachinePrompt Tests

    private static PetStateMachinePrompt CreatePrompt() =>
        new(new PetStateRegistry([
            new IdleState(), new LearningState(), new OrganizingState(), new RestingState(),
            new ReflectingState(), new SocialState(), new PanicState(), new DispatchingState(),
        ]));

    [Fact]
    public void BuildSystemPrompt_ContainsAllStates()
    {
        var prompt = CreatePrompt().BuildSystemPrompt();

        prompt.Should().Contain("Idle");
        prompt.Should().Contain("Learning");
        prompt.Should().Contain("Organizing");
        prompt.Should().Contain("Resting");
        prompt.Should().Contain("Reflecting");
        prompt.Should().Contain("Social");
        prompt.Should().Contain("Panic");
        prompt.Should().Contain("Dispatching");
    }

    [Fact]
    public void BuildSystemPrompt_ContainsAllActionTypes()
    {
        var prompt = CreatePrompt().BuildSystemPrompt();

        prompt.Should().Contain("FetchWeb");
        prompt.Should().Contain("SummarizeToMemory");
        prompt.Should().Contain("OrganizeMemory");
        prompt.Should().Contain("ReflectOnSession");
        prompt.Should().Contain("EvolvePrompts");
        prompt.Should().Contain("NotifyUser");
        prompt.Should().Contain("DelegateToAgent");
    }

    [Fact]
    public void BuildSystemPrompt_ContainsOutputFormat()
    {
        var prompt = CreatePrompt().BuildSystemPrompt();

        prompt.Should().Contain("newState");
        prompt.Should().Contain("emotionShift");
        prompt.Should().Contain("plannedActions");
        prompt.Should().Contain("JSON");
    }

    [Fact]
    public void BuildUserPrompt_ContainsAllSections()
    {
        var report = CreateTestReport();

        var prompt = CreatePrompt().BuildUserPrompt(report);

        prompt.Should().Contain("当前状态");
        prompt.Should().Contain("当前情绪");
        prompt.Should().Contain("速率配额");
        prompt.Should().Contain("可用 Provider");
        prompt.Should().Contain("可用 Agent");
        prompt.Should().Contain("Pet 私有 RAG");
        prompt.Should().Contain("时间信息");
    }

    [Fact]
    public void BuildUserPrompt_IncludesSessionId()
    {
        var report = CreateTestReport();
        var prompt = CreatePrompt().BuildUserPrompt(report);
        prompt.Should().Contain(SessionId);
    }

    [Fact]
    public void BuildUserPrompt_IncludesRateLimitInfo()
    {
        var report = CreateTestReport();
        var prompt = CreatePrompt().BuildUserPrompt(report);
        prompt.Should().Contain("5/100");
        prompt.Should().Contain("95");
    }

    [Fact]
    public void BuildUserPrompt_IncludesRecentMessages()
    {
        var report = CreateTestReport(recentMessages: ["用户: 你好", "助手: 你好！有什么可以帮你的？"]);
        var prompt = CreatePrompt().BuildUserPrompt(report);
        prompt.Should().Contain("最近会话消息");
        prompt.Should().Contain("用户: 你好");
    }

    [Fact]
    public void BuildUserPrompt_OmitsRecentMessages_WhenEmpty()
    {
        var report = CreateTestReport(recentMessages: []);
        var prompt = CreatePrompt().BuildUserPrompt(report);
        prompt.Should().NotContain("最近会话消息");
    }

    [Fact]
    public void BuildUserPrompt_ShowsNeverHeartbeat_WhenNull()
    {
        var report = CreateTestReport(hasHeartbeat: false);
        var prompt = CreatePrompt().BuildUserPrompt(report);
        prompt.Should().Contain("从未执行");
    }

    #endregion

    #region EvaluateAsync Tests

    [Fact]
    public async Task EvaluateAsync_RateExhausted_ReturnsRestingWithoutLlmCall()
    {
        await SetupPetAsync();

        var machine = CreateStateMachineForRateLimitTests();
        var report = CreateTestReport(rateLimitExhausted: true);

        var decision = await machine.EvaluateAsync(report);

        decision.NewState.Should().Be(PetBehaviorState.Resting);
        decision.Reason.Should().Contain("耗尽");
        decision.PlannedActions.Should().BeEmpty();

        // 验证状态已持久化
        var state = await _stateStore.LoadAsync(SessionId);
        state!.BehaviorState.Should().Be(PetBehaviorState.Resting);
    }

    [Fact]
    public async Task EvaluateAsync_RateExhausted_UpdatesEmotion()
    {
        await SetupPetAsync();

        var machine = CreateStateMachineForRateLimitTests();
        var report = CreateTestReport(rateLimitExhausted: true);

        await machine.EvaluateAsync(report);

        var emotion = await _emotionStore.GetCurrentAsync(SessionId);
        // Default 50 + (-10 alertness, -5 mood)
        emotion.Alertness.Should().Be(40);
        emotion.Mood.Should().Be(45);
    }

    /// <summary>
    /// 创建一个用于速率限制测试的 PetStateMachine 实例。
    /// modelSelector 和 clientFactory 不需要实际工作，因为速率超限时会跳过 LLM 调用。
    /// </summary>
    private PetStateMachine CreateStateMachineForRateLimitTests()
    {
        TestConfigFixture.EnsureInitialized();
        var rateLimiter = new PetRateLimiter(_stateStore);
        var providerStore = new ProviderConfigStore();
        var providerRouter = new ProviderRouter();
        var modelSelector = new PetModelSelector(providerStore, providerRouter);
        var clientFactory = new ProviderClientFactory([]);

        var smPrompt = new PetStateMachinePrompt(new PetStateRegistry([
            new IdleState(), new LearningState(), new OrganizingState(), new RestingState(),
            new ReflectingState(), new SocialState(), new PanicState(), new DispatchingState(),
        ]));

        return new PetStateMachine(
            rateLimiter, modelSelector, _stateStore, _emotionStore,
            clientFactory, smPrompt, NullLogger<PetStateMachine>.Instance);
    }

    #endregion

    #region Helpers

    private async Task SetupPetAsync()
    {
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
        await _emotionStore.SaveAsync(SessionId, EmotionState.Default);
    }

    private static PetSelfAwarenessReport CreateTestReport(
        bool rateLimitExhausted = false,
        IReadOnlyList<string>? recentMessages = null,
        DateTimeOffset? lastHeartbeat = null,
        bool hasHeartbeat = true)
    {
        var heartbeat = hasHeartbeat
            ? (lastHeartbeat ?? DateTimeOffset.UtcNow.AddMinutes(-10))
            : (DateTimeOffset?)null;

        return new PetSelfAwarenessReport
        {
            SessionId = SessionId,
            BehaviorState = PetBehaviorState.Idle,
            EmotionState = EmotionState.Default,
            BehaviorMode = BehaviorMode.Normal,
            RateLimitStatus = new RateLimitStatus(
                MaxCalls: 100,
                UsedCalls: rateLimitExhausted ? 100 : 5,
                RemainingCalls: rateLimitExhausted ? 0 : 95,
                WindowStart: DateTimeOffset.UtcNow.AddHours(-1),
                WindowEnd: DateTimeOffset.UtcNow.AddHours(4),
                IsExhausted: rateLimitExhausted),
            EnabledProviderCount = 2,
            AvailableProviders =
            [
                new ProviderSummary("gpt-4o", "GPT-4o", "gpt-4o", 90, "Fast", 2.5m, 10m, true),
                new ProviderSummary("claude-3", "Claude 3", "claude-3-sonnet", 85, "Medium", 3m, 15m, false),
            ],
            PreferredProviderId = null,
            EnabledAgentCount = 1,
            AvailableAgents =
            [
                new AgentSummary("default-agent", "Default", "通用对话 Agent", true),
            ],
            HasPetRag = true,
            PetRagChunkCount = 42,
            RecentMessageSummaries = recentMessages ?? ["用户: 今天天气怎么样？", "助手: 今天晴天，温度适宜。"],
            Timestamp = DateTimeOffset.UtcNow,
            LastHeartbeatAt = heartbeat,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
    }

    #endregion
}
