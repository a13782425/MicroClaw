using FluentAssertions;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Pet;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Storage;
using MicroClaw.Sessions;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// O-3-11：PetContext 和 PetContextFactory 的单元测试。
/// <para>
/// 覆盖范围：
/// <list type="bullet">
///   <item>PetContext 状态机（Active / Disabled / IsEnabled）</item>
///   <item>UpdateEmotion(delta) / UpdateEmotion(state) / UpdateBehaviorState</item>
///   <item>IsDirty / ClearDirty</item>
///   <item>Dispose 幂等性与操作后抛异常</item>
///   <item>PetContextFactory.LoadAsync — 文件缺失 / 完整 / 无情绪记录场景</item>
/// </list>
/// </para>
/// </summary>
public sealed class PetContextTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();
    private readonly PetStateStore _stateStore;
    private readonly EmotionStore _emotionStore;
    private readonly PetContextFactory _factory;

    private const string SessionId = "ctx-test-session";

    public PetContextTests()
    {
        _stateStore = new PetStateStore(_tempDir.Path);
        _emotionStore = new EmotionStore(_tempDir.Path);
        _factory = new PetContextFactory(_stateStore, _emotionStore);
    }

    public void Dispose() => _tempDir.Dispose();

    // ── 辅助方法 ─────────────────────────────────────────────────────────────

    private PetContext CreateContext(bool enabled = true, PetBehaviorState behaviorState = PetBehaviorState.Idle)
    {
        PetState state = new() { SessionId = SessionId, BehaviorState = behaviorState };
        PetConfig config = new() { Enabled = enabled };
        MicroSession microSession = CreateSession(SessionId, isApproved: true);
        return new PetContext(microSession, state, config, EmotionState.Default, PetContextState.Active);
    }

    private static MicroSession CreateSession(string sessionId, bool isApproved)
        => MicroSession.Reconstitute(
            id: sessionId,
            title: sessionId,
            providerId: "provider-1",
            isApproved: isApproved,
            channelType: Configuration.Options.ChannelType.Web,
            channelId: "web",
            createdAt: DateTimeOffset.UtcNow);

    // ══════════════════════════════════════════════════════════════════════════
    //  PetContext — 初始状态
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void New_StateIsActive()
    {
        var ctx = CreateContext();

        ctx.State.Should().Be(PetContextState.Active);
    }

    [Fact]
    public void New_IsEnabledWhenConfigEnabled()
    {
        var ctx = CreateContext(enabled: true);

        ctx.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void New_IsNotEnabledWhenConfigDisabled()
    {
        var ctx = CreateContext(enabled: false);

        ctx.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void New_IsDirtyIsFalse()
    {
        var ctx = CreateContext();

        ctx.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void New_EmotionIsDefault()
    {
        var ctx = CreateContext();

        ctx.Emotion.Should().Be(EmotionState.Default);
    }

    [Fact]
    public void New_BehaviorStateMatchesInput()
    {
        var ctx = CreateContext(behaviorState: PetBehaviorState.Learning);

        ctx.PetState.BehaviorState.Should().Be(PetBehaviorState.Learning);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PetContext — UpdateEmotion(EmotionDelta)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void UpdateEmotion_Delta_AppliesDelta()
    {
        var ctx = CreateContext();
        var delta = new EmotionDelta(Alertness: 10, Mood: -5, Curiosity: 20, Confidence: 0);

        ctx.UpdateEmotion(delta);

        ctx.Emotion.Alertness.Should().Be(EmotionState.DefaultValue + 10);
        ctx.Emotion.Mood.Should().Be(EmotionState.DefaultValue - 5);
        ctx.Emotion.Curiosity.Should().Be(EmotionState.DefaultValue + 20);
        ctx.Emotion.Confidence.Should().Be(EmotionState.DefaultValue);
    }

    [Fact]
    public void UpdateEmotion_Delta_MarksDirty()
    {
        var ctx = CreateContext();

        ctx.UpdateEmotion(new EmotionDelta(Mood: 5));

        ctx.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void UpdateEmotion_Delta_ClampsToRange()
    {
        var ctx = CreateContext();
        // 尝试将值推到 100 以上和 0 以下
        var delta = new EmotionDelta(Alertness: 100, Mood: -100, Curiosity: 100, Confidence: -100);

        ctx.UpdateEmotion(delta);

        ctx.Emotion.Alertness.Should().Be(100);
        ctx.Emotion.Mood.Should().Be(0);
        ctx.Emotion.Curiosity.Should().Be(100);
        ctx.Emotion.Confidence.Should().Be(0);
    }

    [Fact]
    public void UpdateEmotion_Delta_AfterDispose_ThrowsObjectDisposedException()
    {
        var ctx = CreateContext();
        ctx.Dispose();

        var act = () => ctx.UpdateEmotion(EmotionDelta.Zero);

        act.Should().Throw<ObjectDisposedException>();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PetContext — UpdateEmotion(EmotionState)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void UpdateEmotion_State_ReplacesEmotion()
    {
        var ctx = CreateContext();
        var newEmotion = new EmotionState(alertness: 80, mood: 60, curiosity: 40, confidence: 70);

        ctx.UpdateEmotion(newEmotion);

        ctx.Emotion.Should().Be(newEmotion);
    }

    [Fact]
    public void UpdateEmotion_State_MarksDirty()
    {
        var ctx = CreateContext();

        ctx.UpdateEmotion(new EmotionState(alertness: 80));

        ctx.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void UpdateEmotion_State_AfterDispose_ThrowsObjectDisposedException()
    {
        var ctx = CreateContext();
        ctx.Dispose();

        var act = () => ctx.UpdateEmotion(EmotionState.Default);

        act.Should().Throw<ObjectDisposedException>();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PetContext — UpdateBehaviorState
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void UpdateBehaviorState_UpdatesStateField()
    {
        var ctx = CreateContext(behaviorState: PetBehaviorState.Idle);

        ctx.UpdateBehaviorState(PetBehaviorState.Dispatching);

        ctx.PetState.BehaviorState.Should().Be(PetBehaviorState.Dispatching);
    }

    [Fact]
    public void UpdateBehaviorState_MarksDirty()
    {
        var ctx = CreateContext();

        ctx.UpdateBehaviorState(PetBehaviorState.Learning);

        ctx.IsDirty.Should().BeTrue();
    }

    [Fact]
    public void UpdateBehaviorState_UpdatesTimestamp()
    {
        var ctx = CreateContext();
        var before = DateTimeOffset.UtcNow;

        ctx.UpdateBehaviorState(PetBehaviorState.Dispatching);

        ctx.PetState.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void UpdateBehaviorState_AfterDispose_ThrowsObjectDisposedException()
    {
        var ctx = CreateContext();
        ctx.Dispose();

        var act = () => ctx.UpdateBehaviorState(PetBehaviorState.Idle);

        act.Should().Throw<ObjectDisposedException>();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PetContext — IsDirty / ClearDirty
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClearDirty_AfterUpdate_ResetsDirtyFlag()
    {
        var ctx = CreateContext();
        ctx.UpdateEmotion(new EmotionDelta(Mood: 5));
        ctx.IsDirty.Should().BeTrue();

        ctx.ClearDirty();

        ctx.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void MarkDirty_SetsDirtyFlag()
    {
        var ctx = CreateContext();

        ctx.MarkDirty();

        ctx.IsDirty.Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PetContext — Dispose
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Dispose_SetsStateToDisabled()
    {
        var ctx = CreateContext();

        ctx.Dispose();

        ctx.State.Should().Be(PetContextState.Disabled);
    }

    [Fact]
    public void Dispose_IsEnabledReturnsFalse()
    {
        var ctx = CreateContext(enabled: true);

        ctx.Dispose();

        ctx.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var ctx = CreateContext();

        ctx.Dispose();
        var act = () => ctx.Dispose();

        act.Should().NotThrow();
        ctx.State.Should().Be(PetContextState.Disabled);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  PetContextFactory — LoadAsync
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task LoadAsync_NoPetFiles_ReturnsNull()
    {
        // 未写任何文件
        var result = await _factory.LoadAsync("no-such-session");

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_StateExistsButNoConfig_ReturnsNull()
    {
        // 只写 state，不写 config
        var state = new PetState { SessionId = SessionId };
        await _stateStore.SaveAsync(state);

        var result = await _factory.LoadAsync(SessionId);

        result.Should().BeNull();
    }

    [Fact]
    public async Task LoadAsync_BothFilesExist_ReturnsPetContext()
    {
        await WritePetFilesAsync(SessionId, enabled: true);

        var ctx = await _factory.LoadAsync(SessionId);

        ctx.Should().NotBeNull();
        ctx!.State.Should().Be(PetContextState.Active);
        ctx.PetState.SessionId.Should().Be(SessionId);
    }

    [Fact]
    public async Task LoadAsync_ConfigEnabled_IsEnabledTrue()
    {
        await WritePetFilesAsync(SessionId, enabled: true);

        var ctx = await _factory.LoadAsync(SessionId);

        ctx!.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_ConfigDisabled_IsEnabledFalse()
    {
        await WritePetFilesAsync(SessionId, enabled: false);

        var ctx = await _factory.LoadAsync(SessionId);

        ctx!.IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_NoEmotionRecord_UsesDefaultEmotion()
    {
        // 写 state 和 config，但不写 emotion 文件
        await WritePetFilesAsync(SessionId, enabled: true, writeEmotion: false);

        var ctx = await _factory.LoadAsync(SessionId);

        ctx.Should().NotBeNull();
        ctx!.Emotion.Should().Be(EmotionState.Default);
    }

    [Fact]
    public async Task LoadAsync_WithSavedEmotion_RestoresEmotion()
    {
        await WritePetFilesAsync(SessionId, enabled: true);
        var savedEmotion = new EmotionState(alertness: 70, mood: 80, curiosity: 60, confidence: 90);
        await _emotionStore.SaveAsync(SessionId, savedEmotion);

        var ctx = await _factory.LoadAsync(SessionId);

        ctx.Should().NotBeNull();
        ctx!.Emotion.Should().Be(savedEmotion);
    }

    [Fact]
    public async Task LoadAsync_WithSavedBehaviorState_RestoresBehaviorState()
    {
        var desiredState = new PetState
        {
            SessionId = SessionId,
            BehaviorState = PetBehaviorState.Learning,
        };
        await _stateStore.SaveAsync(desiredState);
        await _stateStore.SaveConfigAsync(SessionId, new PetConfig { Enabled = true });

        var ctx = await _factory.LoadAsync(SessionId);

        ctx.Should().NotBeNull();
        ctx!.PetState.BehaviorState.Should().Be(PetBehaviorState.Learning);
    }

    [Fact]
    public async Task LoadAsync_EmptySessionId_ThrowsArgumentException()
    {
        var act = () => _factory.LoadAsync("");

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ── 测试辅助 ─────────────────────────────────────────────────────────────

    private async Task WritePetFilesAsync(string sessionId, bool enabled, bool writeEmotion = true)
    {
        var state = new PetState { SessionId = sessionId };
        await _stateStore.SaveAsync(state);
        await _stateStore.SaveConfigAsync(sessionId, new PetConfig { Enabled = enabled });
        if (writeEmotion)
            await _emotionStore.SaveAsync(sessionId, EmotionState.Default);
    }
}
