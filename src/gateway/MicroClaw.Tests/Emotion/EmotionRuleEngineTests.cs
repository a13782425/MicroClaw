using FluentAssertions;
using MicroClaw.Emotion;

namespace MicroClaw.Tests.Emotion;

public class EmotionRuleEngineTests
{
    // ── 构造函数 ──

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new EmotionRuleEngine(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DefaultCtor_UsesDefaultRules()
    {
        var engine = new EmotionRuleEngine();
        engine.GetDelta(EmotionEventType.MessageSuccess).Should().NotBe(EmotionDelta.Zero);
    }

    // ── DefaultRules 内置规则完整性 ──

    [Theory]
    [InlineData(EmotionEventType.MessageSuccess)]
    [InlineData(EmotionEventType.MessageFailed)]
    [InlineData(EmotionEventType.ToolSuccess)]
    [InlineData(EmotionEventType.ToolError)]
    [InlineData(EmotionEventType.UserSatisfied)]
    [InlineData(EmotionEventType.UserDissatisfied)]
    [InlineData(EmotionEventType.TaskCompleted)]
    [InlineData(EmotionEventType.TaskFailed)]
    public void DefaultRules_ContainsRuleForEveryEventType(EmotionEventType eventType)
    {
        EmotionRuleEngine.DefaultRules.Should().Contain(r => r.EventType == eventType);
    }

    // ── GetDelta：内置规则方向验证 ──

    [Fact]
    public void GetDelta_MessageSuccess_IncreasesMoodAndConfidence()
    {
        var engine = new EmotionRuleEngine();
        var delta = engine.GetDelta(EmotionEventType.MessageSuccess);
        delta.Mood.Should().BePositive();
        delta.Confidence.Should().BePositive();
    }

    [Fact]
    public void GetDelta_MessageFailed_IncreasesAlertnessDecreasesMoodAndConfidence()
    {
        var engine = new EmotionRuleEngine();
        var delta = engine.GetDelta(EmotionEventType.MessageFailed);
        delta.Alertness.Should().BePositive();
        delta.Mood.Should().BeNegative();
        delta.Confidence.Should().BeNegative();
    }

    [Fact]
    public void GetDelta_ToolSuccess_IncreasesCuriosityAndConfidence()
    {
        var engine = new EmotionRuleEngine();
        var delta = engine.GetDelta(EmotionEventType.ToolSuccess);
        delta.Curiosity.Should().BePositive();
        delta.Confidence.Should().BePositive();
    }

    [Fact]
    public void GetDelta_ToolError_IncreasesAlertnessDecreasesMoodAndConfidence()
    {
        var engine = new EmotionRuleEngine();
        var delta = engine.GetDelta(EmotionEventType.ToolError);
        delta.Alertness.Should().BePositive();
        delta.Mood.Should().BeNegative();
        delta.Confidence.Should().BeNegative();
    }

    [Fact]
    public void GetDelta_UserSatisfied_IncreasesMoodAndConfidence()
    {
        var engine = new EmotionRuleEngine();
        var delta = engine.GetDelta(EmotionEventType.UserSatisfied);
        delta.Mood.Should().BePositive();
        delta.Confidence.Should().BePositive();
    }

    [Fact]
    public void GetDelta_UserDissatisfied_DecreasesMoodAndConfidence()
    {
        var engine = new EmotionRuleEngine();
        var delta = engine.GetDelta(EmotionEventType.UserDissatisfied);
        delta.Mood.Should().BeNegative();
        delta.Confidence.Should().BeNegative();
    }

    [Fact]
    public void GetDelta_TaskCompleted_IncreasesMoodAndConfidence()
    {
        var engine = new EmotionRuleEngine();
        var delta = engine.GetDelta(EmotionEventType.TaskCompleted);
        delta.Mood.Should().BePositive();
        delta.Confidence.Should().BePositive();
    }

    [Fact]
    public void GetDelta_TaskFailed_IncreasesAlertnesssDecreasesMoodAndConfidence()
    {
        var engine = new EmotionRuleEngine();
        var delta = engine.GetDelta(EmotionEventType.TaskFailed);
        delta.Alertness.Should().BePositive();
        delta.Mood.Should().BeNegative();
        delta.Confidence.Should().BeNegative();
    }

    // ── GetDelta：无匹配规则时返回 Zero ──

    [Fact]
    public void GetDelta_NoMatchingRule_ReturnsZero()
    {
        var options = new EmotionRuleEngineOptions
        {
            UseDefaultRules = false,
            CustomRules = []
        };
        var engine = new EmotionRuleEngine(options);
        engine.GetDelta(EmotionEventType.MessageSuccess).Should().Be(EmotionDelta.Zero);
    }

    // ── 自定义规则：追加与覆盖 ──

    [Fact]
    public void CustomRule_AppendedToDefaults_MergesWithDefault()
    {
        var extraRule = new EmotionRule(
            EmotionEventType.MessageSuccess,
            new EmotionDelta(Mood: +10));

        var options = new EmotionRuleEngineOptions
        {
            UseDefaultRules = true,
            CustomRules = [extraRule]
        };
        var engine = new EmotionRuleEngine(options);

        // 内置 MessageSuccess Mood +3，追加 +10，合并应为 +13
        var delta = engine.GetDelta(EmotionEventType.MessageSuccess);
        delta.Mood.Should().Be(13);
    }

    [Fact]
    public void CustomRuleOnly_DisablingDefaults_UsesOnlyCustom()
    {
        var customRule = new EmotionRule(
            EmotionEventType.ToolError,
            new EmotionDelta(Alertness: +99));

        var options = new EmotionRuleEngineOptions
        {
            UseDefaultRules = false,
            CustomRules = [customRule]
        };
        var engine = new EmotionRuleEngine(options);

        var delta = engine.GetDelta(EmotionEventType.ToolError);
        delta.Alertness.Should().Be(99);
        delta.Mood.Should().Be(0); // 没有内置规则
    }

    [Fact]
    public void MultipleCustomRulesForSameEvent_AreMerged()
    {
        var options = new EmotionRuleEngineOptions
        {
            UseDefaultRules = false,
            CustomRules =
            [
                new(EmotionEventType.TaskCompleted, new EmotionDelta(Mood: +5)),
                new(EmotionEventType.TaskCompleted, new EmotionDelta(Confidence: +7)),
            ]
        };
        var engine = new EmotionRuleEngine(options);

        var delta = engine.GetDelta(EmotionEventType.TaskCompleted);
        delta.Mood.Should().Be(5);
        delta.Confidence.Should().Be(7);
    }

    // ── Evaluate ──

    [Fact]
    public void Evaluate_NullState_Throws()
    {
        var engine = new EmotionRuleEngine();
        var act = () => engine.Evaluate(null!, EmotionEventType.MessageSuccess);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_AppliesDeltaToCurrentState()
    {
        var options = new EmotionRuleEngineOptions
        {
            UseDefaultRules = false,
            CustomRules = [new(EmotionEventType.UserSatisfied, new EmotionDelta(Mood: +20))]
        };
        var engine = new EmotionRuleEngine(options);
        var initial = new EmotionState(mood: 40);

        var result = engine.Evaluate(initial, EmotionEventType.UserSatisfied);

        result.Mood.Should().Be(60);
        result.Alertness.Should().Be(initial.Alertness); // 未变
    }

    [Fact]
    public void Evaluate_NoMatchingRule_ReturnsUnchangedState()
    {
        var options = new EmotionRuleEngineOptions { UseDefaultRules = false };
        var engine = new EmotionRuleEngine(options);
        var initial = new EmotionState(alertness: 30, mood: 70, curiosity: 55, confidence: 65);

        var result = engine.Evaluate(initial, EmotionEventType.ToolSuccess);

        result.Should().Be(initial);
    }

    [Fact]
    public void Evaluate_DoesNotMutateOriginalState()
    {
        var engine = new EmotionRuleEngine();
        var original = new EmotionState(mood: 50);

        _ = engine.Evaluate(original, EmotionEventType.UserDissatisfied);

        original.Mood.Should().Be(50); // 不可变，原值不变
    }

    // ── EmotionRuleEngineOptions 默认值 ──

    [Fact]
    public void Options_Defaults_UseDefaultRulesTrue_CustomRulesEmpty()
    {
        var opts = new EmotionRuleEngineOptions();
        opts.UseDefaultRules.Should().BeTrue();
        opts.CustomRules.Should().BeEmpty();
    }

    // ── EmotionRule record ──

    [Fact]
    public void EmotionRule_StoresEventTypeAndDelta()
    {
        var delta = new EmotionDelta(Mood: +5);
        var rule = new EmotionRule(EmotionEventType.ToolSuccess, delta, "test rule");

        rule.EventType.Should().Be(EmotionEventType.ToolSuccess);
        rule.Delta.Should().Be(delta);
        rule.Description.Should().Be("test rule");
    }

    [Fact]
    public void EmotionRule_DefaultDescription_IsEmpty()
    {
        var rule = new EmotionRule(EmotionEventType.MessageSuccess, EmotionDelta.Zero);
        rule.Description.Should().BeEmpty();
    }
}
