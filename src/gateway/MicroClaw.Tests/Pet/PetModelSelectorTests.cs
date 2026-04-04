using FluentAssertions;
using MicroClaw.Pet.Decision;
using MicroClaw.Providers;
using MicroClaw.Tests.Fixtures;
using NSubstitute;

namespace MicroClaw.Tests.Pet;

/// <summary>
/// PetModelSelector 单元测试：
/// - 按场景正确映射路由策略
/// - 有 PreferredProviderId 时优先使用
/// - PreferredProviderId 不可用时回退路由
/// - 回退链中 Preferred 提升到首位
/// </summary>
[Collection("Config")]
public sealed class PetModelSelectorTests
{
    private readonly ProviderConfigStore _providerStore;
    private readonly IProviderRouter _router;
    private readonly PetModelSelector _selector;

    private static readonly ProviderConfig ProviderA = new()
    {
        Id = "provider-a",
        DisplayName = "Provider A (cheap)",
        ModelName = "gpt-4o-mini",
        IsEnabled = true,
        IsDefault = true,
        Capabilities = new ProviderCapabilities
        {
            QualityScore = 40,
            LatencyTier = LatencyTier.Low,
            InputPricePerMToken = 0.15m,
            OutputPricePerMToken = 0.6m,
        }
    };

    private static readonly ProviderConfig ProviderB = new()
    {
        Id = "provider-b",
        DisplayName = "Provider B (quality)",
        ModelName = "gpt-4o",
        IsEnabled = true,
        Capabilities = new ProviderCapabilities
        {
            QualityScore = 90,
            LatencyTier = LatencyTier.Medium,
            InputPricePerMToken = 2.5m,
            OutputPricePerMToken = 10m,
        }
    };

    private static readonly ProviderConfig ProviderDisabled = new()
    {
        Id = "provider-disabled",
        DisplayName = "Disabled",
        ModelName = "disabled-model",
        IsEnabled = false,
    };

    public PetModelSelectorTests()
    {
        TestConfigFixture.EnsureInitialized();
        _providerStore = new ProviderConfigStore();
        _router = new ProviderRouter();
        _selector = new PetModelSelector(_providerStore, _router);

        // 添加测试 Provider
        _providerStore.Add(ProviderA);
        _providerStore.Add(ProviderB);
    }

    // ── 策略映射 ──

    [Theory]
    [InlineData(PetModelScenario.Heartbeat, ProviderRoutingStrategy.CostFirst)]
    [InlineData(PetModelScenario.Dispatch, ProviderRoutingStrategy.Default)]
    [InlineData(PetModelScenario.Learning, ProviderRoutingStrategy.QualityFirst)]
    [InlineData(PetModelScenario.Reflecting, ProviderRoutingStrategy.QualityFirst)]
    [InlineData(PetModelScenario.PromptEvolution, ProviderRoutingStrategy.QualityFirst)]
    public void MapScenarioToStrategy_ReturnsCorrectStrategy(PetModelScenario scenario, ProviderRoutingStrategy expected)
    {
        PetModelSelector.MapScenarioToStrategy(scenario).Should().Be(expected);
    }

    // ── Select ──

    [Fact]
    public void Select_Heartbeat_PrefersCheapProvider()
    {
        var result = _selector.Select(PetModelScenario.Heartbeat);

        result.Should().NotBeNull();
        // CostFirst → 成本最低的 Provider 排第一
        result!.Capabilities.InputPricePerMToken.Should().BeLessThan(ProviderB.Capabilities.InputPricePerMToken!.Value);
    }

    [Fact]
    public void Select_Learning_PrefersQualityProvider()
    {
        var result = _selector.Select(PetModelScenario.Learning);

        result.Should().NotBeNull();
        // QualityFirst → 质量最高的 Provider 排第一
        result!.Capabilities.QualityScore.Should().BeGreaterThanOrEqualTo(ProviderA.Capabilities.QualityScore);
    }

    [Fact]
    public void Select_WithPreferred_ReturnsPreferredProvider()
    {
        var providers = _providerStore.All;
        string providerBId = providers.First(p => p.ModelName == "gpt-4o").Id;

        var result = _selector.Select(PetModelScenario.Heartbeat, preferredProviderId: providerBId);

        result.Should().NotBeNull();
        result!.Id.Should().Be(providerBId);
    }

    [Fact]
    public void Select_WithNonExistentPreferred_FallsBackToStrategy()
    {
        var result = _selector.Select(PetModelScenario.Dispatch, preferredProviderId: "non-existent");

        result.Should().NotBeNull();
        // 回退到 Default 策略
    }

    // ── GetFallbackChain ──

    [Fact]
    public void GetFallbackChain_ReturnsMultipleProviders()
    {
        var chain = _selector.GetFallbackChain(PetModelScenario.Dispatch);

        chain.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void GetFallbackChain_WithPreferred_MovesToFirst()
    {
        var providers = _providerStore.All;
        string providerBId = providers.First(p => p.ModelName == "gpt-4o").Id;

        var chain = _selector.GetFallbackChain(PetModelScenario.Heartbeat, preferredProviderId: providerBId);

        chain.Should().NotBeEmpty();
        chain[0].Id.Should().Be(providerBId);
    }
}
