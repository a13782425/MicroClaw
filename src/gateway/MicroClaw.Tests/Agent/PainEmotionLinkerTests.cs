using FluentAssertions;
using MicroClaw.Agent;
using MicroClaw.Emotion;
using MicroClaw.Infrastructure;
using MicroClaw.Safety;
using NSubstitute;

namespace MicroClaw.Tests.Agent;

public class PainEmotionLinkerTests
{
    private static PainMemory MakePain(PainSeverity severity, string agentId = "agent-1")
        => PainMemory.Create(agentId, "trigger", "consequence", "strategy", severity);

    // ── 构造参数校验 ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullEmotionStore_Throws()
    {
        var engine = Substitute.For<IEmotionRuleEngine>();
        var act = () => new PainEmotionLinker(null!, engine);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullRuleEngine_Throws()
    {
        var store = Substitute.For<IEmotionStore>();
        var act = () => new PainEmotionLinker(store, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── LinkAsync：严重度过滤 ──────────────────────────────────────────────────

    [Fact]
    public async Task LinkAsync_LowSeverity_DoesNotUpdateEmotion()
    {
        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        var linker = new PainEmotionLinker(store, engine);

        await linker.LinkAsync(MakePain(PainSeverity.Low));

        await store.DidNotReceive().GetCurrentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<EmotionState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkAsync_MediumSeverity_DoesNotUpdateEmotion()
    {
        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        var linker = new PainEmotionLinker(store, engine);

        await linker.LinkAsync(MakePain(PainSeverity.Medium));

        await store.DidNotReceive().GetCurrentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<EmotionState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkAsync_HighSeverity_CallsRuleEngineWithPainOccurredHigh()
    {
        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        var current = EmotionState.Default;
        var updated = EmotionState.Default with { Alertness = 72 };
        store.GetCurrentAsync("agent-1", Arg.Any<CancellationToken>()).Returns(current);
        engine.Evaluate(current, EmotionEventType.PainOccurredHigh).Returns(updated);

        var linker = new PainEmotionLinker(store, engine);
        await linker.LinkAsync(MakePain(PainSeverity.High));

        engine.Received(1).Evaluate(current, EmotionEventType.PainOccurredHigh);
        await store.Received(1).SaveAsync("agent-1", updated, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkAsync_CriticalSeverity_CallsRuleEngineWithPainOccurredCritical()
    {
        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        var current = EmotionState.Default;
        var updated = EmotionState.Default with { Alertness = 82, Confidence = 22 };
        store.GetCurrentAsync("agent-1", Arg.Any<CancellationToken>()).Returns(current);
        engine.Evaluate(current, EmotionEventType.PainOccurredCritical).Returns(updated);

        var linker = new PainEmotionLinker(store, engine);
        await linker.LinkAsync(MakePain(PainSeverity.Critical));

        engine.Received(1).Evaluate(current, EmotionEventType.PainOccurredCritical);
        await store.Received(1).SaveAsync("agent-1", updated, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkAsync_NullMemory_Throws()
    {
        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        var linker = new PainEmotionLinker(store, engine);

        var act = () => linker.LinkAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task LinkAsync_HighSeverity_UsesAgentIdFromMemory()
    {
        const string agentId = "my-special-agent";
        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        store.GetCurrentAsync(agentId, Arg.Any<CancellationToken>()).Returns(EmotionState.Default);
        engine.Evaluate(Arg.Any<EmotionState>(), Arg.Any<EmotionEventType>()).Returns(EmotionState.Default);

        var linker = new PainEmotionLinker(store, engine);
        await linker.LinkAsync(MakePain(PainSeverity.High, agentId));

        await store.Received(1).GetCurrentAsync(agentId, Arg.Any<CancellationToken>());
        await store.Received(1).SaveAsync(agentId, Arg.Any<EmotionState>(), Arg.Any<CancellationToken>());
    }

    // ── 端到端：规则引擎真实实例 ────────────────────────────────────────────────

    [Fact]
    public async Task LinkAsync_HighPain_WithRealRuleEngine_UpdatesAlertness()
    {
        var emotionStoreSubstitute = Substitute.For<IEmotionStore>();
        var ruleEngine = new EmotionRuleEngine();
        EmotionState initial = EmotionState.Default; // alertness=50
        emotionStoreSubstitute.GetCurrentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(initial);

        EmotionState? saved = null;
        emotionStoreSubstitute
            .SaveAsync(Arg.Any<string>(), Arg.Do<EmotionState>(s => saved = s), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var linker = new PainEmotionLinker(emotionStoreSubstitute, ruleEngine);
        await linker.LinkAsync(MakePain(PainSeverity.High));

        saved.Should().NotBeNull();
        saved!.Alertness.Should().BeGreaterThan(initial.Alertness,
            "高级痛觉应使警觉度上升");
        saved.Confidence.Should().BeLessThan(initial.Confidence,
            "高级痛觉应使信心下降");
    }

    [Fact]
    public async Task LinkAsync_CriticalPain_WithRealRuleEngine_TriggersCautiousMode()
    {
        var emotionStoreSubstitute = Substitute.For<IEmotionStore>();
        var ruleEngine = new EmotionRuleEngine();
        var mapper = new EmotionBehaviorMapper();
        EmotionState initial = EmotionState.Default;
        emotionStoreSubstitute.GetCurrentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(initial);

        EmotionState? saved = null;
        emotionStoreSubstitute
            .SaveAsync(Arg.Any<string>(), Arg.Do<EmotionState>(s => saved = s), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var linker = new PainEmotionLinker(emotionStoreSubstitute, ruleEngine);
        await linker.LinkAsync(MakePain(PainSeverity.Critical));

        saved.Should().NotBeNull();
        BehaviorProfile profile = mapper.GetProfile(saved!);
        profile.Mode.Should().Be(BehaviorMode.Cautious,
            "Critical 痛觉后情绪应映射到「谨慎」行为模式");
    }

    // ── EmotionEventType 新枚举值 ────────────────────────────────────────────

    [Fact]
    public void EmotionEventType_PainOccurredHigh_ExistsAndIsDistinct()
    {
        var all = Enum.GetValues<EmotionEventType>();
        all.Should().Contain(EmotionEventType.PainOccurredHigh);
        all.Should().Contain(EmotionEventType.PainOccurredCritical);
        ((int)EmotionEventType.PainOccurredHigh).Should().NotBe((int)EmotionEventType.PainOccurredCritical);
    }

    [Fact]
    public void EmotionRuleEngine_DefaultRules_ContainPainOccurredEvents()
    {
        var rules = EmotionRuleEngine.DefaultRules;
        rules.Should().Contain(r => r.EventType == EmotionEventType.PainOccurredHigh);
        rules.Should().Contain(r => r.EventType == EmotionEventType.PainOccurredCritical);
    }
}
