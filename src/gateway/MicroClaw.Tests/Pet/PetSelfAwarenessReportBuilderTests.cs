using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Pet;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;
using MicroClaw.Tests.Fixtures;
using NSubstitute;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// PetSelfAwarenessReportBuilder 单元测试：
/// - 正确聚合 Pet 状态、情绪、速率限制
/// - 正确过滤 Agent 和 Provider
/// - Pet 不存在时返回 null
/// </summary>
[Collection("Config")]
public sealed class PetSelfAwarenessReportBuilderTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PetStateStore _stateStore;
    private readonly EmotionStore _emotionStore;
    private readonly IEmotionBehaviorMapper _behaviorMapper;
    private readonly PetRateLimiter _rateLimiter;
    private readonly ProviderConfigStore _providerStore;
    private readonly AgentStore _agentStore;
    private readonly PetSelfAwarenessReportBuilder _builder;

    private const string SessionId = "report-test-session";

    public PetSelfAwarenessReportBuilderTests()
    {
        TestConfigFixture.EnsureInitialized();

        _stateStore = new PetStateStore(_tempDir.Path);
        _emotionStore = new EmotionStore(_tempDir.Path);
        _behaviorMapper = new EmotionBehaviorMapper();
        _rateLimiter = new PetRateLimiter(_stateStore);
        _providerStore = new ProviderConfigStore();
        _agentStore = new AgentStore();

        _builder = new PetSelfAwarenessReportBuilder(
            _stateStore, _emotionStore, _behaviorMapper,
            _rateLimiter, _providerStore, _agentStore);
    }

    public void Dispose() => _tempDir.Dispose();

    private async Task SetupPetAsync()
    {
        var state = new PetState
        {
            SessionId = SessionId,
            BehaviorState = PetBehaviorState.Learning,
            EmotionState = new EmotionState(alertness: 70, mood: 60, curiosity: 80, confidence: 55),
            LlmCallCount = 10,
            WindowStart = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-2),
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _stateStore.SaveAsync(state);

        var config = new PetConfig
        {
            Enabled = true,
            MaxLlmCallsPerWindow = 100,
            WindowHours = 5.0,
            PreferredProviderId = "preferred-provider",
        };
        await _stateStore.SaveConfigAsync(SessionId, config);

        // 保存情绪
        await _emotionStore.SaveAsync(SessionId, new EmotionState(70, 60, 80, 55));
    }

    [Fact]
    public async Task Build_ReturnsNull_WhenPetDoesNotExist()
    {
        var report = await _builder.BuildAsync("non-existent-session");
        report.Should().BeNull();
    }

    [Fact]
    public async Task Build_ReturnsReport_WithCorrectPetState()
    {
        await SetupPetAsync();

        var report = await _builder.BuildAsync(SessionId);

        report.Should().NotBeNull();
        report!.SessionId.Should().Be(SessionId);
        report.BehaviorState.Should().Be(PetBehaviorState.Learning);
    }

    [Fact]
    public async Task Build_ReturnsReport_WithEmotionState()
    {
        await SetupPetAsync();

        var report = await _builder.BuildAsync(SessionId);

        report.Should().NotBeNull();
        report!.EmotionState.Alertness.Should().Be(70);
        report.EmotionState.Curiosity.Should().Be(80);
    }

    [Fact]
    public async Task Build_ReturnsReport_WithRateLimitStatus()
    {
        await SetupPetAsync();

        var report = await _builder.BuildAsync(SessionId);

        report.Should().NotBeNull();
        report!.RateLimitStatus.Should().NotBeNull();
        report.RateLimitStatus!.MaxCalls.Should().Be(100);
        report.RateLimitStatus.UsedCalls.Should().Be(10);
        report.RateLimitStatus.RemainingCalls.Should().Be(90);
    }

    [Fact]
    public async Task Build_ReturnsReport_WithPreferredProviderId()
    {
        await SetupPetAsync();

        var report = await _builder.BuildAsync(SessionId);

        report.Should().NotBeNull();
        report!.PreferredProviderId.Should().Be("preferred-provider");
    }

    [Fact]
    public async Task Build_IncludesRecentMessages_WhenProvided()
    {
        await SetupPetAsync();

        var messages = new[] { "[user] 你好", "[assistant] 你好！" };
        var report = await _builder.BuildAsync(SessionId, recentMessageSummaries: messages);

        report.Should().NotBeNull();
        report!.RecentMessageSummaries.Should().HaveCount(2);
    }

    [Fact]
    public async Task Build_IncludesPetRagInfo()
    {
        await SetupPetAsync();

        var report = await _builder.BuildAsync(SessionId, petRagChunkCount: 42);

        report.Should().NotBeNull();
        report!.HasPetRag.Should().BeTrue();
        report.PetRagChunkCount.Should().Be(42);
    }

    [Fact]
    public async Task Build_WithZeroRagChunks_ShowsNoPetRag()
    {
        await SetupPetAsync();

        var report = await _builder.BuildAsync(SessionId, petRagChunkCount: 0);

        report.Should().NotBeNull();
        report!.HasPetRag.Should().BeFalse();
    }

    [Fact]
    public async Task Build_HasTimestamp()
    {
        await SetupPetAsync();
        var before = DateTimeOffset.UtcNow;

        var report = await _builder.BuildAsync(SessionId);

        report.Should().NotBeNull();
        report!.Timestamp.Should().BeOnOrAfter(before);
    }
}
