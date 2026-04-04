using FluentAssertions;
using MicroClaw.Pet;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.Storage;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// PetRateLimiter 单元测试：
/// - 滑动窗口计数准确
/// - 超限拒绝
/// - 窗口过期重置
/// - Pet 不存在时返回 false / null
/// </summary>
public sealed class PetRateLimiterTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PetStateStore _stateStore;
    private readonly PetRateLimiter _limiter;

    private const string SessionId = "rate-limit-test-session";

    public PetRateLimiterTests()
    {
        _stateStore = new PetStateStore(_tempDir.Path);
        _limiter = new PetRateLimiter(_stateStore);
    }

    public void Dispose() => _tempDir.Dispose();

    private async Task SetupPetAsync(int maxCalls = 5, double windowHours = 1.0, int currentCount = 0, DateTimeOffset? windowStart = null)
    {
        var state = new PetState
        {
            SessionId = SessionId,
            BehaviorState = PetBehaviorState.Idle,
            EmotionState = EmotionState.Default,
            LlmCallCount = currentCount,
            WindowStart = windowStart ?? DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _stateStore.SaveAsync(state);

        var config = new PetConfig
        {
            Enabled = true,
            MaxLlmCallsPerWindow = maxCalls,
            WindowHours = windowHours,
        };
        await _stateStore.SaveConfigAsync(SessionId, config);
    }

    [Fact]
    public async Task TryAcquire_ReturnsFalse_WhenPetDoesNotExist()
    {
        bool result = await _limiter.TryAcquireAsync("non-existent-session");
        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquire_ReturnsTrue_WhenUnderLimit()
    {
        await SetupPetAsync(maxCalls: 10);

        bool result = await _limiter.TryAcquireAsync(SessionId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquire_IncrementsCount()
    {
        await SetupPetAsync(maxCalls: 10);

        await _limiter.TryAcquireAsync(SessionId);

        var state = await _stateStore.LoadAsync(SessionId);
        state.Should().NotBeNull();
        state!.LlmCallCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquire_ReturnsFalse_WhenAtLimit()
    {
        await SetupPetAsync(maxCalls: 3, currentCount: 3);

        bool result = await _limiter.TryAcquireAsync(SessionId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquire_ReturnsFalse_WhenOverLimit()
    {
        await SetupPetAsync(maxCalls: 3, currentCount: 5);

        bool result = await _limiter.TryAcquireAsync(SessionId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquire_ConsecutiveCalls_RespectLimit()
    {
        await SetupPetAsync(maxCalls: 3);

        (await _limiter.TryAcquireAsync(SessionId)).Should().BeTrue();  // 1
        (await _limiter.TryAcquireAsync(SessionId)).Should().BeTrue();  // 2
        (await _limiter.TryAcquireAsync(SessionId)).Should().BeTrue();  // 3
        (await _limiter.TryAcquireAsync(SessionId)).Should().BeFalse(); // 4 — 超限
    }

    [Fact]
    public async Task TryAcquire_ResetsWindow_WhenExpired()
    {
        // 窗口 1 小时，从 2 小时前开始，已用 3 次
        var expiredWindowStart = DateTimeOffset.UtcNow.AddHours(-2);
        await SetupPetAsync(maxCalls: 3, windowHours: 1.0, currentCount: 3, windowStart: expiredWindowStart);

        // 窗口已过期，应该重置并允许
        bool result = await _limiter.TryAcquireAsync(SessionId);

        result.Should().BeTrue();

        var state = await _stateStore.LoadAsync(SessionId);
        state!.LlmCallCount.Should().Be(1); // 重置后首次调用
    }

    [Fact]
    public async Task GetStatus_ReturnsNull_WhenPetDoesNotExist()
    {
        var status = await _limiter.GetStatusAsync("non-existent-session");
        status.Should().BeNull();
    }

    [Fact]
    public async Task GetStatus_ReturnsCorrectRemainingCalls()
    {
        await SetupPetAsync(maxCalls: 10, currentCount: 3);

        var status = await _limiter.GetStatusAsync(SessionId);

        status.Should().NotBeNull();
        status!.MaxCalls.Should().Be(10);
        status.UsedCalls.Should().Be(3);
        status.RemainingCalls.Should().Be(7);
        status.IsExhausted.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatus_ShowsExhausted_WhenAtLimit()
    {
        await SetupPetAsync(maxCalls: 5, currentCount: 5);

        var status = await _limiter.GetStatusAsync(SessionId);

        status.Should().NotBeNull();
        status!.RemainingCalls.Should().Be(0);
        status.IsExhausted.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatus_ResetsWindow_WhenExpired()
    {
        var expiredWindowStart = DateTimeOffset.UtcNow.AddHours(-3);
        await SetupPetAsync(maxCalls: 10, windowHours: 1.0, currentCount: 10, windowStart: expiredWindowStart);

        var status = await _limiter.GetStatusAsync(SessionId);

        status.Should().NotBeNull();
        status!.UsedCalls.Should().Be(0);
        status.RemainingCalls.Should().Be(10);
        status.IsExhausted.Should().BeFalse();
    }

    [Fact]
    public async Task TryAcquire_UsesDefaultConfig_WhenConfigFileMissing()
    {
        // 只创建 state，不创建 config
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

        // 无 config 文件时使用 PetConfig 默认值（MaxLlmCallsPerWindow=100）
        bool result = await _limiter.TryAcquireAsync(SessionId);
        result.Should().BeTrue();
    }
}
