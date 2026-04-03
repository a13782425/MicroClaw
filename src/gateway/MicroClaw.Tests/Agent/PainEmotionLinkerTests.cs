using FluentAssertions;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Infrastructure;
using MicroClaw.Pet;
using MicroClaw.Pet.Emotion;
using MicroClaw.Safety;
using NSubstitute;

namespace MicroClaw.Tests.Agent;

public class PainEmotionLinkerTests
{
    private const string AgentId = "agent-1";
    private const string SessionId = "session-1";

    private static PainMemory MakePain(PainSeverity severity, string agentId = AgentId)
        => PainMemory.Create(agentId, "trigger", "consequence", "strategy", severity);

    private static SessionInfo MakeSession(string sessionId, string agentId) =>
        new SessionInfo(
            Id: sessionId,
            Title: "test",
            ProviderId: "test",
            IsApproved: true,
            ChannelType: ChannelType.Web,
            ChannelId: "web",
            CreatedAt: DateTimeOffset.UtcNow,
            AgentId: agentId);

    // ── 构造参数校验 ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullEmotionStore_Throws()
    {
        var engine = Substitute.For<IEmotionRuleEngine>();
        var reader = Substitute.For<IAllSessionsReader>();
        var act = () => new PainEmotionLinker(null!, engine, reader);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullRuleEngine_Throws()
    {
        var store = Substitute.For<IEmotionStore>();
        var reader = Substitute.For<IAllSessionsReader>();
        var act = () => new PainEmotionLinker(store, null!, reader);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullSessionsReader_Throws()
    {
        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        var act = () => new PainEmotionLinker(store, engine, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── LinkAsync：严重度过滤 ──────────────────────────────────────────────────

    [Fact]
    public async Task LinkAsync_LowSeverity_DoesNotUpdateEmotion()
    {
        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        var reader = Substitute.For<IAllSessionsReader>();
        var linker = new PainEmotionLinker(store, engine, reader);

        await linker.LinkAsync(MakePain(PainSeverity.Low));

        reader.DidNotReceive().GetAll();
        await store.DidNotReceive().GetCurrentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<EmotionState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkAsync_MediumSeverity_DoesNotUpdateEmotion()
    {
        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        var reader = Substitute.For<IAllSessionsReader>();
        var linker = new PainEmotionLinker(store, engine, reader);

        await linker.LinkAsync(MakePain(PainSeverity.Medium));

        reader.DidNotReceive().GetAll();
        await store.DidNotReceive().GetCurrentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<EmotionState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkAsync_HighSeverity_CallsRuleEngineWithPainOccurredHigh()
    {
        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        var reader = Substitute.For<IAllSessionsReader>();
        var current = EmotionState.Default;
        var updated = EmotionState.Default with { Alertness = 72 };

        reader.GetAll().Returns(new[] { MakeSession(SessionId, AgentId) }.ToList().AsReadOnly());
        store.GetCurrentAsync(SessionId, Arg.Any<CancellationToken>()).Returns(current);
        engine.Evaluate(current, EmotionEventType.PainOccurredHigh).Returns(updated);

        var linker = new PainEmotionLinker(store, engine, reader);
        await linker.LinkAsync(MakePain(PainSeverity.High));

        engine.Received(1).Evaluate(current, EmotionEventType.PainOccurredHigh);
        await store.Received(1).SaveAsync(SessionId, updated, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkAsync_CriticalSeverity_CallsRuleEngineWithPainOccurredCritical()
    {
        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        var reader = Substitute.For<IAllSessionsReader>();
        var current = EmotionState.Default;
        var updated = EmotionState.Default with { Alertness = 82, Confidence = 22 };

        reader.GetAll().Returns(new[] { MakeSession(SessionId, AgentId) }.ToList().AsReadOnly());
        store.GetCurrentAsync(SessionId, Arg.Any<CancellationToken>()).Returns(current);
        engine.Evaluate(current, EmotionEventType.PainOccurredCritical).Returns(updated);

        var linker = new PainEmotionLinker(store, engine, reader);
        await linker.LinkAsync(MakePain(PainSeverity.Critical));

        engine.Received(1).Evaluate(current, EmotionEventType.PainOccurredCritical);
        await store.Received(1).SaveAsync(SessionId, updated, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkAsync_NullMemory_Throws()
    {
        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        var reader = Substitute.For<IAllSessionsReader>();
        var linker = new PainEmotionLinker(store, engine, reader);

        var act = () => linker.LinkAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task LinkAsync_HighSeverity_UsesAgentIdFromMemory()
    {
        const string myAgentId = "my-special-agent";
        const string mySessionId = "my-special-session";

        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        var reader = Substitute.For<IAllSessionsReader>();

        reader.GetAll().Returns(new[] { MakeSession(mySessionId, myAgentId) }.ToList().AsReadOnly());
        store.GetCurrentAsync(mySessionId, Arg.Any<CancellationToken>()).Returns(EmotionState.Default);
        engine.Evaluate(Arg.Any<EmotionState>(), Arg.Any<EmotionEventType>()).Returns(EmotionState.Default);

        var linker = new PainEmotionLinker(store, engine, reader);
        await linker.LinkAsync(MakePain(PainSeverity.High, myAgentId));

        await store.Received(1).GetCurrentAsync(mySessionId, Arg.Any<CancellationToken>());
        await store.Received(1).SaveAsync(mySessionId, Arg.Any<EmotionState>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LinkAsync_NoSessionForAgent_DoesNotCallStore()
    {
        var store = Substitute.For<IEmotionStore>();
        var engine = Substitute.For<IEmotionRuleEngine>();
        var reader = Substitute.For<IAllSessionsReader>();

        // 没有任何 session 匹配该 agentId
        reader.GetAll().Returns(new[] { MakeSession("other-session", "other-agent") }.ToList().AsReadOnly());

        var linker = new PainEmotionLinker(store, engine, reader);
        await linker.LinkAsync(MakePain(PainSeverity.High));

        await store.DidNotReceive().GetCurrentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await store.DidNotReceive().SaveAsync(Arg.Any<string>(), Arg.Any<EmotionState>(), Arg.Any<CancellationToken>());
    }

    // ── 端到端：规则引擎真实实例 ────────────────────────────────────────────────

    [Fact]
    public async Task LinkAsync_HighPain_WithRealRuleEngine_UpdatesAlertness()
    {
        var emotionStoreSubstitute = Substitute.For<IEmotionStore>();
        var ruleEngine = new EmotionRuleEngine();
        var reader = Substitute.For<IAllSessionsReader>();
        EmotionState initial = EmotionState.Default;

        reader.GetAll().Returns(new[] { MakeSession(SessionId, AgentId) }.ToList().AsReadOnly());
        emotionStoreSubstitute.GetCurrentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(initial);

        EmotionState? saved = null;
        emotionStoreSubstitute
            .SaveAsync(Arg.Any<string>(), Arg.Do<EmotionState>(s => saved = s), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var linker = new PainEmotionLinker(emotionStoreSubstitute, ruleEngine, reader);
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
        var reader = Substitute.For<IAllSessionsReader>();
        EmotionState initial = EmotionState.Default;

        reader.GetAll().Returns(new[] { MakeSession(SessionId, AgentId) }.ToList().AsReadOnly());
        emotionStoreSubstitute.GetCurrentAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(initial);

        EmotionState? saved = null;
        emotionStoreSubstitute
            .SaveAsync(Arg.Any<string>(), Arg.Do<EmotionState>(s => saved = s), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var linker = new PainEmotionLinker(emotionStoreSubstitute, ruleEngine, reader);
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
