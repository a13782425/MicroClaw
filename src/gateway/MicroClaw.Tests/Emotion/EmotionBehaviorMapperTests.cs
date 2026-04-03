using FluentAssertions;
using MicroClaw.Pet.Emotion;

namespace MicroClaw.Tests.Emotion;

public class EmotionBehaviorMapperTests
{
    // ── 构造函数 ──

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        var act = () => new EmotionBehaviorMapper(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DefaultCtor_UsesDefaultOptions()
    {
        var mapper = new EmotionBehaviorMapper();
        // 默认状态（四维均为50）→ Normal
        var profile = mapper.GetProfile(EmotionState.Default);
        profile.Mode.Should().Be(BehaviorMode.Normal);
    }

    // ── GetProfile：参数验证 ──

    [Fact]
    public void GetProfile_NullState_Throws()
    {
        var mapper = new EmotionBehaviorMapper();
        var act = () => mapper.GetProfile(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    // ── 谨慎模式（最高优先级）──

    [Fact]
    public void GetProfile_HighAlertness_ReturnsCautious()
    {
        var mapper = new EmotionBehaviorMapper();
        // 警觉度 >= 70 → Cautious
        var state = new EmotionState(alertness: 70, mood: 80, curiosity: 80, confidence: 80);
        mapper.GetProfile(state).Mode.Should().Be(BehaviorMode.Cautious);
    }

    [Fact]
    public void GetProfile_VeryHighAlertness_ReturnsCautious()
    {
        var mapper = new EmotionBehaviorMapper();
        var state = new EmotionState(alertness: 100, mood: 50, curiosity: 50, confidence: 50);
        mapper.GetProfile(state).Mode.Should().Be(BehaviorMode.Cautious);
    }

    [Fact]
    public void GetProfile_LowConfidence_ReturnsCautious()
    {
        var mapper = new EmotionBehaviorMapper();
        // 信心 <= 30 → Cautious（即使其他维度正常）
        var state = new EmotionState(alertness: 50, mood: 70, curiosity: 70, confidence: 30);
        mapper.GetProfile(state).Mode.Should().Be(BehaviorMode.Cautious);
    }

    [Fact]
    public void GetProfile_HighAlertnessOverridesExplore_ReturnsCautious()
    {
        var mapper = new EmotionBehaviorMapper();
        // 好奇心高 + 心情好，但警觉度也高 → 谨慎优先
        var state = new EmotionState(alertness: 75, mood: 80, curiosity: 90, confidence: 60);
        mapper.GetProfile(state).Mode.Should().Be(BehaviorMode.Cautious);
    }

    // ── 探索模式 ──

    [Fact]
    public void GetProfile_HighCuriosityAndGoodMood_ReturnsExplore()
    {
        var mapper = new EmotionBehaviorMapper();
        // 好奇心 >= 70 且 心情 >= 60 → Explore
        var state = new EmotionState(alertness: 50, mood: 70, curiosity: 80, confidence: 60);
        mapper.GetProfile(state).Mode.Should().Be(BehaviorMode.Explore);
    }

    [Fact]
    public void GetProfile_HighCuriosityButLowMood_NotExplore()
    {
        var mapper = new EmotionBehaviorMapper();
        // 好奇心高但心情差 → 不触发探索
        var state = new EmotionState(alertness: 50, mood: 50, curiosity: 80, confidence: 60);
        mapper.GetProfile(state).Mode.Should().NotBe(BehaviorMode.Explore);
    }

    [Fact]
    public void GetProfile_GoodMoodButLowCuriosity_NotExplore()
    {
        var mapper = new EmotionBehaviorMapper();
        var state = new EmotionState(alertness: 50, mood: 80, curiosity: 60, confidence: 60);
        mapper.GetProfile(state).Mode.Should().NotBe(BehaviorMode.Explore);
    }

    // ── 休息模式 ──

    [Fact]
    public void GetProfile_LowAlertnessAndLowMood_ReturnsRest()
    {
        var mapper = new EmotionBehaviorMapper();
        // 警觉度 <= 30 且 心情 <= 40 → Rest
        var state = new EmotionState(alertness: 20, mood: 30, curiosity: 50, confidence: 50);
        mapper.GetProfile(state).Mode.Should().Be(BehaviorMode.Rest);
    }

    [Fact]
    public void GetProfile_LowAlertnessBetterMood_NotRest()
    {
        var mapper = new EmotionBehaviorMapper();
        var state = new EmotionState(alertness: 25, mood: 50, curiosity: 50, confidence: 50);
        mapper.GetProfile(state).Mode.Should().NotBe(BehaviorMode.Rest);
    }

    [Fact]
    public void GetProfile_LowMoodButHigherAlertness_NotRest()
    {
        var mapper = new EmotionBehaviorMapper();
        var state = new EmotionState(alertness: 45, mood: 30, curiosity: 50, confidence: 50);
        mapper.GetProfile(state).Mode.Should().NotBe(BehaviorMode.Rest);
    }

    // ── 正常模式 ──

    [Fact]
    public void GetProfile_DefaultState_ReturnsNormal()
    {
        var mapper = new EmotionBehaviorMapper();
        mapper.GetProfile(EmotionState.Default).Mode.Should().Be(BehaviorMode.Normal);
    }

    [Fact]
    public void GetProfile_MidRangeAllDimensions_ReturnsNormal()
    {
        var mapper = new EmotionBehaviorMapper();
        var state = new EmotionState(alertness: 50, mood: 55, curiosity: 55, confidence: 55);
        mapper.GetProfile(state).Mode.Should().Be(BehaviorMode.Normal);
    }

    // ── BehaviorProfile 参数正确性 ──

    [Fact]
    public void CautiousProfile_HasLowerTemperatureThanNormal()
    {
        BehaviorProfile.DefaultCautious.Temperature.Should()
            .BeLessThan(BehaviorProfile.DefaultNormal.Temperature);
    }

    [Fact]
    public void ExploreProfile_HasHigherTemperatureThanNormal()
    {
        BehaviorProfile.DefaultExplore.Temperature.Should()
            .BeGreaterThan(BehaviorProfile.DefaultNormal.Temperature);
    }

    [Fact]
    public void CautiousProfile_HasNonEmptySystemPromptSuffix()
    {
        BehaviorProfile.DefaultCautious.SystemPromptSuffix.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ExploreProfile_HasNonEmptySystemPromptSuffix()
    {
        BehaviorProfile.DefaultExplore.SystemPromptSuffix.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void RestProfile_HasNonEmptySystemPromptSuffix()
    {
        BehaviorProfile.DefaultRest.SystemPromptSuffix.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void NormalProfile_HasEmptySystemPromptSuffix()
    {
        BehaviorProfile.DefaultNormal.SystemPromptSuffix.Should().BeEmpty();
    }

    // ── 自定义阈值 ──

    [Fact]
    public void CustomThreshold_LowerCautiousAlertness_TriggersEarlier()
    {
        var opts = new EmotionBehaviorMapperOptions { CautiousAlertnessThreshold = 50 };
        var mapper = new EmotionBehaviorMapper(opts);
        // 警觉度刚好 50，按自定义阈值应触发谨慎
        var state = new EmotionState(alertness: 50, mood: 50, curiosity: 50, confidence: 60);
        mapper.GetProfile(state).Mode.Should().Be(BehaviorMode.Cautious);
    }

    [Fact]
    public void CustomThreshold_ExploreRequiresLowerCuriosity_TriggersEarlier()
    {
        var opts = new EmotionBehaviorMapperOptions { ExploreMinCuriosity = 60, ExploreMinMood = 60 };
        var mapper = new EmotionBehaviorMapper(opts);
        var state = new EmotionState(alertness: 50, mood: 65, curiosity: 65, confidence: 60);
        mapper.GetProfile(state).Mode.Should().Be(BehaviorMode.Explore);
    }

    // ── 自定义 Profile ──

    [Fact]
    public void CustomProfile_OverridesDefaultForMode()
    {
        var customNormal = new BehaviorProfile(BehaviorMode.Normal, 0.42f, 0.88f, "custom");
        var opts = new EmotionBehaviorMapperOptions { NormalProfile = customNormal };
        var mapper = new EmotionBehaviorMapper(opts);

        var profile = mapper.GetProfile(EmotionState.Default);

        profile.Temperature.Should().BeApproximately(0.42f, 0.001f);
        profile.SystemPromptSuffix.Should().Be("custom");
    }

    // ── EmotionBehaviorMapperOptions 默认值 ──

    [Fact]
    public void Options_Defaults_AreReasonable()
    {
        var opts = new EmotionBehaviorMapperOptions();
        opts.CautiousAlertnessThreshold.Should().Be(70);
        opts.CautiousConfidenceThreshold.Should().Be(30);
        opts.ExploreMinCuriosity.Should().Be(70);
        opts.ExploreMinMood.Should().Be(60);
        opts.RestMaxAlertness.Should().Be(30);
        opts.RestMaxMood.Should().Be(40);
    }

    // ── BehaviorProfile record ──

    [Fact]
    public void BehaviorProfile_StoresAllProperties()
    {
        var profile = new BehaviorProfile(BehaviorMode.Rest, 0.5f, 0.85f, "keep it short");
        profile.Mode.Should().Be(BehaviorMode.Rest);
        profile.Temperature.Should().BeApproximately(0.5f, 0.001f);
        profile.TopP.Should().BeApproximately(0.85f, 0.001f);
        profile.SystemPromptSuffix.Should().Be("keep it short");
    }

    // ── GetProfile 返回正确 Profile 实例 ──

    [Fact]
    public void GetProfile_Cautious_ReturnsConfiguredCautiousProfile()
    {
        var mapper = new EmotionBehaviorMapper();
        var state = new EmotionState(alertness: 80, mood: 50, curiosity: 50, confidence: 50);
        var profile = mapper.GetProfile(state);
        profile.Should().Be(BehaviorProfile.DefaultCautious);
    }

    [Fact]
    public void GetProfile_Explore_ReturnsConfiguredExploreProfile()
    {
        var mapper = new EmotionBehaviorMapper();
        var state = new EmotionState(alertness: 50, mood: 75, curiosity: 80, confidence: 60);
        var profile = mapper.GetProfile(state);
        profile.Should().Be(BehaviorProfile.DefaultExplore);
    }

    [Fact]
    public void GetProfile_Rest_ReturnsConfiguredRestProfile()
    {
        var mapper = new EmotionBehaviorMapper();
        var state = new EmotionState(alertness: 20, mood: 25, curiosity: 50, confidence: 50);
        var profile = mapper.GetProfile(state);
        profile.Should().Be(BehaviorProfile.DefaultRest);
    }
}
