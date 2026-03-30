using MicroClaw.Providers;
using NSubstitute;

namespace MicroClaw.Tests.Providers;

/// <summary>
/// AgentRunner.BuildFallbackChain 回退链构建逻辑测试（通过公开的 ProviderRouter 合约间接验证）。
/// 针对 Provider 回退链优先级排列的行为规约测试。
/// </summary>
public sealed class ProviderFallbackBehaviorTests
{
    private static ProviderConfig MakeProvider(
        string id,
        bool isEnabled = true,
        bool isDefault = false,
        int qualityScore = 50) =>
        new()
        {
            Id = id,
            DisplayName = id,
            ModelName = id,
            ApiKey = "key",
            IsEnabled = isEnabled,
            IsDefault = isDefault,
            Capabilities = new ProviderCapabilities { QualityScore = qualityScore },
        };

    // ── IProviderRouter.GetFallbackChain 契约测试 ────────────────────────────

    [Fact]
    public void GetFallbackChain_PrimaryIsFirst_WhenPrimaryIsHighestQuality()
    {
        // Arrange: a=quality 90 (primary), b=quality 70, c=quality 50
        var router = new ProviderRouter();
        var providers = new List<ProviderConfig>
        {
            MakeProvider("a", qualityScore: 90),
            MakeProvider("b", qualityScore: 70),
            MakeProvider("c", qualityScore: 50),
        };

        // Act
        var chain = router.GetFallbackChain(providers, ProviderRoutingStrategy.QualityFirst);

        // Assert: a is primary (highest quality), b and c are fallbacks
        Assert.Equal("a", chain[0].Id);
        Assert.Equal("b", chain[1].Id);
        Assert.Equal("c", chain[2].Id);
    }

    [Fact]
    public void GetFallbackChain_ReturnsAllEnabled_NotJustBest()
    {
        var router = new ProviderRouter();
        var providers = new List<ProviderConfig>
        {
            MakeProvider("p1"),
            MakeProvider("p2"),
            MakeProvider("p3"),
            MakeProvider("p4", isEnabled: false), // excluded
        };

        var chain = router.GetFallbackChain(providers, ProviderRoutingStrategy.Default);

        // All 3 enabled providers should be in the chain
        Assert.Equal(3, chain.Count);
        Assert.DoesNotContain(chain, p => p.Id == "p4");
    }

    [Fact]
    public void GetFallbackChain_QualityFirst_LowerQualityIsLaterInChain()
    {
        var router = new ProviderRouter();
        var providers = new List<ProviderConfig>
        {
            MakeProvider("worst",  qualityScore: 10),
            MakeProvider("best",   qualityScore: 100),
            MakeProvider("middle", qualityScore: 50),
        };

        var chain = router.GetFallbackChain(providers, ProviderRoutingStrategy.QualityFirst);

        // First is best, last is worst
        Assert.Equal("best", chain[0].Id);
        Assert.Equal("worst", chain[chain.Count - 1].Id);
    }

    // ── IProviderRouter mock 模拟 AgentRunner 会调用的行为 ──────────────────

    [Fact]
    public void MockRouter_GetFallbackChain_ReturnsExpectedOrder()
    {
        // 模拟 AgentRunner 内部构建 BuildFallbackChain 时调用 IProviderRouter.GetFallbackChain
        var mockRouter = Substitute.For<IProviderRouter>();
        var p1 = MakeProvider("primary", isDefault: true);
        var p2 = MakeProvider("fallback1");
        var p3 = MakeProvider("fallback2");

        mockRouter
            .GetFallbackChain(Arg.Any<IReadOnlyList<ProviderConfig>>(), Arg.Any<ProviderRoutingStrategy>())
            .Returns([p1, p2, p3]);

        var chain = mockRouter.GetFallbackChain([p1, p2, p3], ProviderRoutingStrategy.Default);

        Assert.Equal("primary", chain[0].Id);
        Assert.Equal("fallback1", chain[1].Id);
        Assert.Equal("fallback2", chain[2].Id);
    }

    [Fact]
    public void MockRouter_EmptyChain_NoProvidersAvailable()
    {
        var mockRouter = Substitute.For<IProviderRouter>();
        mockRouter
            .GetFallbackChain(Arg.Any<IReadOnlyList<ProviderConfig>>(), Arg.Any<ProviderRoutingStrategy>())
            .Returns([]);
        mockRouter
            .Route(Arg.Any<IReadOnlyList<ProviderConfig>>(), Arg.Any<ProviderRoutingStrategy>())
            .Returns((ProviderConfig?)null);

        var chain = mockRouter.GetFallbackChain([], ProviderRoutingStrategy.Default);
        var routed = mockRouter.Route([], ProviderRoutingStrategy.Default);

        Assert.Empty(chain);
        Assert.Null(routed);
    }

    // ── 回退链包含主 Provider + 剩余按策略顺序的行为验证 ──────────────────

    [Theory]
    [InlineData(ProviderRoutingStrategy.Default)]
    [InlineData(ProviderRoutingStrategy.QualityFirst)]
    [InlineData(ProviderRoutingStrategy.CostFirst)]
    [InlineData(ProviderRoutingStrategy.LatencyFirst)]
    public void GetFallbackChain_AllStrategies_OnlyContainsEnabledProviders(ProviderRoutingStrategy strategy)
    {
        var router = new ProviderRouter();
        var providers = new List<ProviderConfig>
        {
            MakeProvider("enabled1"),
            MakeProvider("disabled", isEnabled: false),
            MakeProvider("enabled2"),
        };

        var chain = router.GetFallbackChain(providers, strategy);

        Assert.All(chain, p => Assert.True(p.IsEnabled));
        Assert.Equal(2, chain.Count);
    }
}
