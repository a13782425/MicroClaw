using FluentAssertions;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Pet;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Storage;
using MicroClaw.Tests.Fixtures;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// O-4-7：PetContext 并发安全测试。
/// <para>
/// 覆盖范围：
/// <list type="bullet">
///   <item>并发 UpdateEmotion — 多线程同时更新同一 PetContext 不抛异常，状态合理</item>
///   <item>并发 UpdateBehaviorState — 多线程并发无异常</item>
///   <item>IsDirty 可见性 — 并发更新后 IsDirty 为 true</item>
///   <item>Session 懒加载竞争 — 多个线程同时为同一 Session 调用 AttachPet（last-write-wins），Session 不为 null</item>
///   <item>已 Dispose 的 PetContext — 并发操作后抛 ObjectDisposedException 而非挂起</item>
/// </list>
/// </para>
/// </summary>
public sealed class PetContextConcurrencyTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    public void Dispose() => _tempDir.Dispose();

    // ── 辅助：直接构造 PetContext ────────────────────────────────────────────

    private static PetContext CreateContext(bool enabled = true)
    {
        var state = new PetState { SessionId = "concurrency-test", BehaviorState = PetBehaviorState.Idle };
        var config = new PetConfig { Enabled = enabled };
        return new PetContext(state, config, EmotionState.Default);
    }

    private static EmotionDelta SampleDelta(int mood = 1) =>
        new EmotionDelta(Alertness: 0, Mood: mood, Curiosity: 0, Confidence: 0);

    // ══════════════════════════════════════════════════════════════════════════
    //  并发 UpdateEmotion
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentUpdateEmotion_NoExceptions()
    {
        using var ctx = CreateContext();
        int concurrency = 50;

        var tasks = Enumerable.Range(0, concurrency).Select(_ =>
            Task.Run(() => ctx.UpdateEmotion(SampleDelta(1))));

        await tasks.Invoking(async t => await Task.WhenAll(t))
            .Should().NotThrowAsync("并发 UpdateEmotion 不应抛出异常");
    }

    [Fact]
    public async Task ConcurrentUpdateEmotion_IsDirtyBecomesTrue()
    {
        using var ctx = CreateContext();

        var tasks = Enumerable.Range(0, 20).Select(_ =>
            Task.Run(() => ctx.UpdateEmotion(SampleDelta(1))));
        await Task.WhenAll(tasks);

        ctx.IsDirty.Should().BeTrue("至少有一次 UpdateEmotion 调用，IsDirty 应为 true");
    }

    [Fact]
    public async Task ConcurrentUpdateEmotion_StateRemainsEnabled()
    {
        using var ctx = CreateContext();

        var tasks = Enumerable.Range(0, 30).Select(_ =>
            Task.Run(() => ctx.UpdateEmotion(SampleDelta(1))));
        await Task.WhenAll(tasks);

        ctx.State.Should().Be(PetContextState.Active, "并发更新不应改变 PetContext 状态");
        ctx.IsEnabled.Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  并发 UpdateBehaviorState
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentUpdateBehaviorState_NoExceptions()
    {
        using var ctx = CreateContext();
        var states = new[] { PetBehaviorState.Idle, PetBehaviorState.Learning, PetBehaviorState.Resting };

        var tasks = Enumerable.Range(0, 60).Select(i =>
            Task.Run(() => ctx.UpdateBehaviorState(states[i % states.Length])));

        await tasks.Invoking(async t => await Task.WhenAll(t))
            .Should().NotThrowAsync("并发 UpdateBehaviorState 不应抛出异常");
    }

    [Fact]
    public async Task ConcurrentMixedUpdates_NoExceptions()
    {
        using var ctx = CreateContext();
        var states = new[] { PetBehaviorState.Idle, PetBehaviorState.Learning };

        var emotionTasks = Enumerable.Range(0, 25).Select(_ =>
            Task.Run(() => ctx.UpdateEmotion(SampleDelta(1))));
        var behaviorTasks = Enumerable.Range(0, 25).Select(i =>
            Task.Run(() => ctx.UpdateBehaviorState(states[i % 2])));

        await Task.WhenAll(emotionTasks.Concat(behaviorTasks));

        ctx.State.Should().Be(PetContextState.Active, "混合并发更新后 PetContext 应仍处于 Active 状态");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Session 懒加载竞争：多线程同时 AttachPet（last-write-wins）
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentAttachPet_LastWriteWins_SessionPetContextNotNull()
    {
        // 模拟 PetRunner 懒加载时多线程同时 AttachPet 的场景
        // 直接使用 Session.Reconstitute，无需 SessionStore（避免 MicroClawConfig 依赖）
        var session = Session.Reconstitute(
            id: "concurrency-attach-test",
            title: "并发附加测试",
            providerId: "provider1",
            isApproved: true,
            channelType: ChannelType.Web,
            channelId: "",
            createdAt: DateTimeOffset.UtcNow);;

        // 并发附加不同的 PetContext 实例（最后一个写入生效）
        var contexts = Enumerable.Range(0, 20).Select(_ => CreateContext()).ToList();

        var tasks = contexts.Select(ctx =>
            Task.Run(() => session.AttachPet(ctx))).ToList();

        await Task.WhenAll(tasks);

        // 不管哪个 ctx 生效，PetContext 不应为 null
        session.PetContext.Should().NotBeNull("并发 AttachPet 后 PetContext 不应为 null（last-write-wins）");
        session.PetContext!.IsEnabled.Should().BeTrue("附加的 PetContext 应处于启用状态");

        // 清理
        foreach (var ctx in contexts) ctx.Dispose();
    }

    [Fact]
    public async Task ConcurrentAttachAndRead_NoCrash()
    {
        // 直接使用 Session.Reconstitute，无需 SessionStore（避免 MicroClawConfig 依赖）
        var session = Session.Reconstitute(
            id: "concurrency-read-test",
            title: "并发读写测试",
            providerId: "p1",
            isApproved: true,
            channelType: ChannelType.Web,
            channelId: "",
            createdAt: DateTimeOffset.UtcNow);;

        // 并发写（AttachPet）+ 并发读（session.PetContext）
        var writeTasks = Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() =>
            {
                using var ctx = CreateContext();
                session.AttachPet(ctx);
            })).ToList();

        var readTasks = Enumerable.Range(0, 10).Select(__ =>
            Task.Run(() =>
            {
                // 读取 PetContext（可能为 null、Active 或 Disabled）
                var pc = session.PetContext;
                // 仅验证不抜异常，不对值做断言（可能 null）
                bool? enabled = pc?.IsEnabled;
                _ = enabled;
            })).ToList();

        await writeTasks.Concat(readTasks).Invoking(async t => await Task.WhenAll(t))
            .Should().NotThrowAsync("并发读写 Session.PetContext 不应抛出异常");
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  Dispose 后的并发操作 → ObjectDisposedException
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AfterDispose_ConcurrentOperations_ThrowObjectDisposedException()
    {
        var ctx = CreateContext();
        ctx.Dispose();  // 已 Dispose

        // 并发尝试更新应全部抛 ObjectDisposedException
        var tasks = Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() =>
            {
                var action = () => ctx.UpdateEmotion(SampleDelta(1));
                action.Should().Throw<ObjectDisposedException>("Dispose 后操作应抛 ObjectDisposedException");
            }));

        await Task.WhenAll(tasks);
    }

    // ══════════════════════════════════════════════════════════════════════════
    //  ClearDirty 与 MarkDirty 并发
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConcurrentDirtyAndClear_NoExceptionOrDeadlock()
    {
        using var ctx = CreateContext();

        var dirtyTasks = Enumerable.Range(0, 30).Select(_ =>
            Task.Run(() => ctx.UpdateEmotion(SampleDelta(1))));
        var clearTasks = Enumerable.Range(0, 10).Select(_ =>
            Task.Run(() => ctx.ClearDirty()));

        await dirtyTasks.Concat(clearTasks).Invoking(async t => await Task.WhenAll(t))
            .Should().NotThrowAsync("并发 MarkDirty/ClearDirty 不应抛出异常或死锁");
    }
}
