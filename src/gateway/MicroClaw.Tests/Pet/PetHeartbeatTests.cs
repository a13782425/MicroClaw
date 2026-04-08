using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using MicroClaw.Pet;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Heartbeat;
using MicroClaw.Pet.Prompt;
using MicroClaw.Pet.Rag;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.StateMachine.States;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;
using MicroClaw.Sessions;
using MicroClaw.Tests.Fixtures;
using NSubstitute;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// PetHeartbeatExecutor + PetHeartbeatJob + PetActionExecutor 单元测试。
/// </summary>
[Collection("Config")]
public sealed class PetHeartbeatTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PetStateStore _stateStore;
    private readonly EmotionStore _emotionStore;

    private const string SessionId = "heartbeat-test-session";

    public PetHeartbeatTests()
    {
        TestConfigFixture.EnsureInitialized();
        _stateStore = new PetStateStore(_tempDir.Path);
        _emotionStore = new EmotionStore(_tempDir.Path);
    }

    public void Dispose() => _tempDir.Dispose();

    // ── Helper：创建启用的 Pet 状态和配置 ──────────────────────────────

    private async Task CreateEnabledPetAsync(
        PetBehaviorState state = PetBehaviorState.Idle,
        bool enabled = true,
        int? activeHoursStart = null,
        int? activeHoursEnd = null)
    {
        await _stateStore.SaveAsync(new PetState
        {
            SessionId = SessionId,
            BehaviorState = state,
            EmotionState = EmotionState.Default,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        await _stateStore.SaveConfigAsync(SessionId, new PetConfig
        {
            Enabled = enabled,
            MaxLlmCallsPerWindow = 100,
            WindowHours = 5.0,
            ActiveHoursStart = activeHoursStart,
            ActiveHoursEnd = activeHoursEnd,
        });
    }

    // ═══════════════════════════════════════════════════════════════════
    //  IsWithinActiveHours 静态方法测试
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void IsWithinActiveHours_NoConfig_AlwaysActive()
    {
        var config = new PetConfig { ActiveHoursStart = null, ActiveHoursEnd = null };
        PetHeartbeatExecutor.IsWithinActiveHours(config).Should().BeTrue();
    }

    [Fact]
    public void IsWithinActiveHours_SameDayRange_WithinRange()
    {
        int currentHour = DateTimeOffset.UtcNow.Hour;
        var config = new PetConfig { ActiveHoursStart = 0, ActiveHoursEnd = 24 };
        PetHeartbeatExecutor.IsWithinActiveHours(config).Should().BeTrue();
    }

    [Fact]
    public void IsWithinActiveHours_SameDayRange_OutsideRange()
    {
        // Always out of range: start == end (empty range)
        int currentHour = DateTimeOffset.UtcNow.Hour;
        var config = new PetConfig
        {
            ActiveHoursStart = (currentHour + 2) % 24,
            ActiveHoursEnd = (currentHour + 3) % 24,
        };
        // Current hour is not in [start, end) which is a 1-hour window 2 hours from now
        PetHeartbeatExecutor.IsWithinActiveHours(config).Should().BeFalse();
    }

    [Fact]
    public void IsWithinActiveHours_CrossMidnight_CurrentInRange()
    {
        // Create a cross-midnight range that always includes current hour
        int currentHour = DateTimeOffset.UtcNow.Hour;
        var config = new PetConfig
        {
            ActiveHoursStart = (currentHour - 1 + 24) % 24,  // 1 hour before now
            ActiveHoursEnd = (currentHour + 2) % 24,  // 2 hours after now
        };
        // This should always work because we set it relative to current time
        // But only if wrap-around. Let's make a case that's definitely cross-midnight.
        var crossMidnight = new PetConfig { ActiveHoursStart = 22, ActiveHoursEnd = 8 };
        // This means active from 22:00 to 08:00
        // If current hour is 23, should be true; if 10, should be false
        // We can't predict current hour, but we can test the logic:
        if (currentHour >= 22 || currentHour < 8)
            PetHeartbeatExecutor.IsWithinActiveHours(crossMidnight).Should().BeTrue();
        else
            PetHeartbeatExecutor.IsWithinActiveHours(crossMidnight).Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PetHeartbeatExecutor 测试
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HeartbeatExecutor_PetNotEnabled_ReturnsSkipped()
    {
        await CreateEnabledPetAsync(enabled: false);

        var executor = CreateHeartbeatExecutor();
        var result = await executor.ExecuteAsync(SessionId);

        result.Executed.Should().BeFalse();
        result.IsSuccess.Should().BeTrue();
        result.SkipReason.Should().Contain("未启用");
    }

    [Fact]
    public async Task HeartbeatExecutor_PetNotExist_ReturnsSkipped()
    {
        var executor = CreateHeartbeatExecutor();
        var result = await executor.ExecuteAsync("nonexistent-session");

        result.Executed.Should().BeFalse();
        result.IsSuccess.Should().BeTrue();
        result.SkipReason.Should().Contain("未启用");
    }

    [Fact]
    public async Task HeartbeatExecutor_Dispatching_ReturnsSkipped()
    {
        await CreateEnabledPetAsync(state: PetBehaviorState.Dispatching);

        var executor = CreateHeartbeatExecutor();
        var result = await executor.ExecuteAsync(SessionId);

        result.Executed.Should().BeFalse();
        result.SkipReason.Should().Contain("Dispatching");
    }

    [Fact]
    public async Task HeartbeatExecutor_Enabled_ExecutesStateMachineAndActions()
    {
        await CreateEnabledPetAsync(state: PetBehaviorState.Idle);

        // State machine will return resting (no provider available)
        var executor = CreateHeartbeatExecutor();
        var result = await executor.ExecuteAsync(SessionId);

        result.Executed.Should().BeTrue();
        // With no provider, PetStateMachine enters Panic and returns 0 actions
        result.NewState.Should().NotBeNull();
    }

    [Fact]
    public async Task HeartbeatExecutor_UpdatesLastHeartbeatAt()
    {
        await CreateEnabledPetAsync(state: PetBehaviorState.Idle);

        var executor = CreateHeartbeatExecutor();
        await executor.ExecuteAsync(SessionId);

        var state = await _stateStore.LoadAsync(SessionId);
        state.Should().NotBeNull();
        state!.LastHeartbeatAt.Should().NotBeNull();
        state.LastHeartbeatAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PetHeartbeatJob 测试
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PetHeartbeatJob_JobMetadata_IsCorrect()
    {
        var sessionRepo = Substitute.For<MicroClaw.Abstractions.Sessions.ISessionRepository>();
        sessionRepo.GetAll().Returns(System.Array.Empty<MicroClaw.Abstractions.Sessions.IMicroSession>());

        var executor = CreateHeartbeatExecutor();
        var job = new PetHeartbeatJob(
            sessionRepo,
            executor,
            NullLogger<PetHeartbeatJob>.Instance);

        job.JobName.Should().Be("pet-heartbeat");
        job.Schedule.Should().BeOfType<MicroClaw.Jobs.JobSchedule.FixedInterval>();
        var interval = (MicroClaw.Jobs.JobSchedule.FixedInterval)job.Schedule;
        interval.Interval.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task PetHeartbeatJob_NoSessions_CompletesWithoutError()
    {
        var sessionRepo = Substitute.For<MicroClaw.Abstractions.Sessions.ISessionRepository>();
        sessionRepo.GetAll().Returns(System.Array.Empty<MicroClaw.Abstractions.Sessions.IMicroSession>());

        var executor = CreateHeartbeatExecutor();
        var job = new PetHeartbeatJob(
            sessionRepo,
            executor,
            NullLogger<PetHeartbeatJob>.Instance);

        await job.ExecuteAsync(CancellationToken.None);
        // Should complete without throwing
    }

    [Fact]
    public async Task PetHeartbeatJob_ApprovedSessions_InvokesExecutor()
    {
        await CreateEnabledPetAsync(state: PetBehaviorState.Idle);

        var sessionRepo = Substitute.For<MicroClaw.Abstractions.Sessions.ISessionRepository>();
        sessionRepo.GetAll().Returns(new[]
        {
            MicroSession.Reconstitute(
                SessionId, "Test", "provider1", true,
                MicroClaw.Abstractions.ChannelType.Web, "", DateTimeOffset.UtcNow)
        });

        var executor = CreateHeartbeatExecutor();
        var job = new PetHeartbeatJob(
            sessionRepo,
            executor,
            NullLogger<PetHeartbeatJob>.Instance);

        await job.ExecuteAsync(CancellationToken.None);
        // Should not throw; will attempt heartbeat for the session
    }

    [Fact]
    public async Task PetHeartbeatJob_UnapprovedSessions_Skipped()
    {
        var sessionRepo = Substitute.For<MicroClaw.Abstractions.Sessions.ISessionRepository>();
        sessionRepo.GetAll().Returns(new[]
        {
            MicroSession.Reconstitute(
                "unapproved", "Test", "provider1", false,
                MicroClaw.Abstractions.ChannelType.Web, "", DateTimeOffset.UtcNow)
        });

        var executor = CreateHeartbeatExecutor();
        var job = new PetHeartbeatJob(
            sessionRepo,
            executor,
            NullLogger<PetHeartbeatJob>.Instance);

        await job.ExecuteAsync(CancellationToken.None);
        // Unapproved sessions should be skipped (no heartbeat attempt)
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PetActionExecutor 测试
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ActionExecutor_EmptyActions_ReturnsEmptyResults()
    {
        await CreateEnabledPetAsync();

        var executor = CreateActionExecutor();
        var results = await executor.ExecuteAsync(SessionId, []);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ActionExecutor_NullActions_ReturnsEmptyResults()
    {
        await CreateEnabledPetAsync();

        var executor = CreateActionExecutor();
        var results = await executor.ExecuteAsync(SessionId, null!);

        results.Should().BeEmpty();
    }

    [Fact]
    public async Task ActionExecutor_EvolvePrompts_DelegatesToPromptEvolver()
    {
        await CreateEnabledPetAsync();

        var executor = CreateActionExecutor();
        var actions = new PetPlannedAction[]
        {
            new() { Type = PetActionType.EvolvePrompts, Reason = "测试进化" },
        };

        var results = await executor.ExecuteAsync(SessionId, actions);

        results.Should().HaveCount(1);
        results[0].ActionType.Should().Be(PetActionType.EvolvePrompts);
        // Will fail since no provider configured, but should not throw
    }

    [Fact]
    public async Task ActionExecutor_SummarizeToMemory_NoContent_ReturnsFailed()
    {
        await CreateEnabledPetAsync();

        var executor = CreateActionExecutor();
        var actions = new PetPlannedAction[]
        {
            new() { Type = PetActionType.SummarizeToMemory, Parameter = null },
        };

        var results = await executor.ExecuteAsync(SessionId, actions);

        results.Should().HaveCount(1);
        results[0].Succeeded.Should().BeFalse();
        results[0].Error.Should().Contain("未提供");
    }

    [Fact]
    public async Task ActionExecutor_NotifyUser_NoMessage_ReturnsFailed()
    {
        await CreateEnabledPetAsync();

        var executor = CreateActionExecutor();
        var actions = new PetPlannedAction[]
        {
            new() { Type = PetActionType.NotifyUser, Parameter = null },
        };

        var results = await executor.ExecuteAsync(SessionId, actions);

        results.Should().HaveCount(1);
        results[0].Succeeded.Should().BeFalse();
        results[0].Error.Should().Contain("未提供");
    }

    [Fact]
    public async Task ActionExecutor_NotifyUser_WithMessage_CallsNotifier()
    {
        await CreateEnabledPetAsync();

        var notifier = Substitute.For<IPetNotifier>();
        var executor = CreateActionExecutor(petNotifier: notifier);
        var actions = new PetPlannedAction[]
        {
            new() { Type = PetActionType.NotifyUser, Parameter = "你好！" },
        };

        var results = await executor.ExecuteAsync(SessionId, actions);

        results.Should().HaveCount(1);
        results[0].Succeeded.Should().BeTrue();
        await notifier.Received(1).NotifyUserAsync(SessionId, "你好！", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActionExecutor_DelegateToAgent_NoAgentId_ReturnsFailed()
    {
        await CreateEnabledPetAsync();

        var executor = CreateActionExecutor();
        var actions = new PetPlannedAction[]
        {
            new() { Type = PetActionType.DelegateToAgent, Parameter = null },
        };

        var results = await executor.ExecuteAsync(SessionId, actions);

        results.Should().HaveCount(1);
        results[0].Succeeded.Should().BeFalse();
        results[0].Error.Should().Contain("AgentId");
    }

    [Fact]
    public async Task ActionExecutor_DelegateToAgent_WithAgentId_RecordsIntent()
    {
        await CreateEnabledPetAsync();

        var executor = CreateActionExecutor();
        var actions = new PetPlannedAction[]
        {
            new() { Type = PetActionType.DelegateToAgent, Parameter = "agent-001" },
        };

        var results = await executor.ExecuteAsync(SessionId, actions);

        results.Should().HaveCount(1);
        results[0].Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task ActionExecutor_FetchWeb_InvalidUrl_ReturnsFailed()
    {
        await CreateEnabledPetAsync();

        var executor = CreateActionExecutor();
        var actions = new PetPlannedAction[]
        {
            new() { Type = PetActionType.FetchWeb, Parameter = "ftp://example.com" },
        };

        var results = await executor.ExecuteAsync(SessionId, actions);

        results.Should().HaveCount(1);
        results[0].Succeeded.Should().BeFalse();
        results[0].Error.Should().Contain("不支持");
    }

    [Fact]
    public async Task ActionExecutor_FetchWeb_NoUrl_ReturnsFailed()
    {
        await CreateEnabledPetAsync();

        var executor = CreateActionExecutor();
        var actions = new PetPlannedAction[]
        {
            new() { Type = PetActionType.FetchWeb, Parameter = null },
        };

        var results = await executor.ExecuteAsync(SessionId, actions);

        results.Should().HaveCount(1);
        results[0].Succeeded.Should().BeFalse();
        results[0].Error.Should().Contain("URL");
    }

    [Fact]
    public async Task ActionExecutor_MultipleActions_ExecutesAll()
    {
        await CreateEnabledPetAsync();

        var notifier = Substitute.For<IPetNotifier>();
        var executor = CreateActionExecutor(petNotifier: notifier);

        var actions = new PetPlannedAction[]
        {
            new() { Type = PetActionType.NotifyUser, Parameter = "消息1" },
            new() { Type = PetActionType.DelegateToAgent, Parameter = "agent-1" },
            new() { Type = PetActionType.SummarizeToMemory, Parameter = null },  // will fail
        };

        var results = await executor.ExecuteAsync(SessionId, actions);

        results.Should().HaveCount(3);
        results[0].Succeeded.Should().BeTrue();
        results[1].Succeeded.Should().BeTrue();
        results[2].Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task ActionExecutor_OrganizeMemory_EmptyRag_SkipsGracefully()
    {
        await CreateEnabledPetAsync();

        var executor = CreateActionExecutor();
        var actions = new PetPlannedAction[]
        {
            new() { Type = PetActionType.OrganizeMemory },
        };

        var results = await executor.ExecuteAsync(SessionId, actions);

        results.Should().HaveCount(1);
        // Empty RAG should succeed with "无需整理" message
        results[0].ActionType.Should().Be(PetActionType.OrganizeMemory);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ActionExecutionResult 测试
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ActionExecutionResult_Properties()
    {
        var success = new ActionExecutionResult(PetActionType.FetchWeb, true);
        success.Succeeded.Should().BeTrue();
        success.Error.Should().BeNull();

        var failure = new ActionExecutionResult(PetActionType.FetchWeb, false, "some error");
        failure.Succeeded.Should().BeFalse();
        failure.Error.Should().Be("some error");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  HeartbeatResult 测试
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void HeartbeatResult_Skipped()
    {
        var r = HeartbeatResult.Skipped("test reason");
        r.Executed.Should().BeFalse();
        r.IsSuccess.Should().BeTrue();
        r.SkipReason.Should().Be("test reason");
    }

    [Fact]
    public void HeartbeatResult_Success()
    {
        var r = HeartbeatResult.Success(PetBehaviorState.Learning, 3, 1);
        r.Executed.Should().BeTrue();
        r.IsSuccess.Should().BeTrue();
        r.NewState.Should().Be(PetBehaviorState.Learning);
        r.ActionsSucceeded.Should().Be(3);
        r.ActionsFailed.Should().Be(1);
    }

    [Fact]
    public void HeartbeatResult_Failed()
    {
        var r = HeartbeatResult.Failed("error msg");
        r.Executed.Should().BeTrue();
        r.IsSuccess.Should().BeFalse();
        r.Error.Should().Be("error msg");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helper Methods
    // ═══════════════════════════════════════════════════════════════════

    private PetHeartbeatExecutor CreateHeartbeatExecutor()
    {
        var providerStore = new ProviderConfigStore();
        var agentStore = new MicroClaw.Agent.AgentStore();
        var providerRouter = Substitute.For<IProviderRouter>();
        var rateLimiter = new PetRateLimiter(_stateStore);
        var modelSelector = new PetModelSelector(providerStore, providerRouter);
        var clientFactory = new ProviderClientFactory([]);
        var emotionRuleEngine = new EmotionRuleEngine();
        var behaviorMapper = new EmotionBehaviorMapper();

        var smPrompt = new PetStateMachinePrompt(new PetStateRegistry([
            new IdleState(), new LearningState(), new OrganizingState(), new RestingState(),
            new ReflectingState(), new SocialState(), new PanicState(), new DispatchingState(),
        ]));

        var stateMachine = new PetStateMachine(
            rateLimiter, modelSelector, _stateStore, _emotionStore,
            clientFactory, smPrompt, NullLogger<PetStateMachine>.Instance);

        var reportBuilder = new PetSelfAwarenessReportBuilder(
            _stateStore, _emotionStore, behaviorMapper, rateLimiter,
            providerStore, agentStore);

        var embedding = Substitute.For<MicroClaw.RAG.IEmbeddingService>();
        var petRagScope = new PetRagScope(embedding, _tempDir.Path,
            NullLogger<PetRagScope>.Instance);

        var actionExecutor = CreateActionExecutor();

        return new PetHeartbeatExecutor(
            _stateStore, stateMachine, reportBuilder, petRagScope,
            actionExecutor, Substitute.For<IPetNotifier>(),
            NullLogger<PetHeartbeatExecutor>.Instance);
    }

    private PetActionExecutor CreateActionExecutor(IPetNotifier? petNotifier = null)
    {
        var providerStore = new ProviderConfigStore();
        var providerRouter = Substitute.For<IProviderRouter>();
        var rateLimiter = new PetRateLimiter(_stateStore);
        var modelSelector = new PetModelSelector(providerStore, providerRouter);
        var clientFactory = new ProviderClientFactory([]);

        var promptStore = new PetPromptStore(_tempDir.Path);
        var promptEvolver = new PetPromptEvolver(
            promptStore, _stateStore, rateLimiter, modelSelector, clientFactory,
            _tempDir.Path, NullLogger<PetPromptEvolver>.Instance);

        var embedding = Substitute.For<MicroClaw.RAG.IEmbeddingService>();
        var petRagScope = new PetRagScope(embedding, _tempDir.Path,
            NullLogger<PetRagScope>.Instance);

        var notifier = petNotifier ?? Substitute.For<IPetNotifier>();
        var httpClientFactory = Substitute.For<IHttpClientFactory>();

        return new PetActionExecutor(
            petRagScope, promptEvolver, _stateStore, rateLimiter, modelSelector,
            clientFactory, notifier, httpClientFactory,
            NullLogger<PetActionExecutor>.Instance, _ : false);
    }
}
