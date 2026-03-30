using MicroClaw.Providers;

namespace MicroClaw.Tests.Providers;

/// <summary>
/// ProviderRouter.GetFallbackChain 单元测试：覆盖四种路由策略、排序、边界条件。
/// </summary>
public sealed class ProviderFallbackChainTests
{
    private readonly ProviderRouter _router = new();

    // ── 测试数据工厂 ────────────────────────────────────────────────────────

    private static ProviderConfig Make(
        string id,
        bool isEnabled = true,
        bool isDefault = false,
        int qualityScore = 50,
        decimal? inputPrice = null,
        decimal? outputPrice = null,
        LatencyTier latency = LatencyTier.Medium) =>
        new()
        {
            Id = id,
            DisplayName = id,
            ModelName = id,
            ApiKey = "test",
            IsEnabled = isEnabled,
            IsDefault = isDefault,
            Capabilities = new ProviderCapabilities
            {
                QualityScore = qualityScore,
                InputPricePerMToken = inputPrice,
                OutputPricePerMToken = outputPrice,
                LatencyTier = latency,
            },
        };

    // ── 空/全禁用 ─────────────────────────────────────────────────────────

    [Fact]
    public void GetFallbackChain_EmptyList_ReturnsEmpty()
    {
        var result = _router.GetFallbackChain([], ProviderRoutingStrategy.Default);
        Assert.Empty(result);
    }

    [Fact]
    public void GetFallbackChain_AllDisabled_ReturnsEmpty()
    {
        var providers = new List<ProviderConfig>
        {
            Make("a", isEnabled: false),
            Make("b", isEnabled: false),
        };
        var result = _router.GetFallbackChain(providers, ProviderRoutingStrategy.Default);
        Assert.Empty(result);
    }

    [Fact]
    public void GetFallbackChain_SingleProvider_ReturnsSingle()
    {
        var providers = new List<ProviderConfig> { Make("only") };
        var result = _router.GetFallbackChain(providers, ProviderRoutingStrategy.Default);
        Assert.Single(result);
        Assert.Equal("only", result[0].Id);
    }

    // ── Default 策略 ────────────────────────────────────────────────────────

    [Fact]
    public void GetFallbackChain_Default_DefaultProviderIsFirst()
    {
        var providers = new List<ProviderConfig>
        {
            Make("a"),
            Make("b", isDefault: true),
            Make("c"),
        };
        var result = _router.GetFallbackChain(providers, ProviderRoutingStrategy.Default);
        Assert.Equal(3, result.Count);
        Assert.Equal("b", result[0].Id);
    }

    [Fact]
    public void GetFallbackChain_Default_DisabledExcluded()
    {
        var providers = new List<ProviderConfig>
        {
            Make("disabled", isEnabled: false),
            Make("enabled"),
        };
        var result = _router.GetFallbackChain(providers, ProviderRoutingStrategy.Default);
        Assert.Single(result);
        Assert.Equal("enabled", result[0].Id);
    }

    [Fact]
    public void GetFallbackChain_Default_ReturnsAllEnabled()
    {
        var providers = new List<ProviderConfig>
        {
            Make("a"),
            Make("b"),
            Make("c", isEnabled: false),
        };
        var result = _router.GetFallbackChain(providers, ProviderRoutingStrategy.Default);
        Assert.Equal(2, result.Count);
        Assert.DoesNotContain(result, p => p.Id == "c");
    }

    // ── QualityFirst 策略 ───────────────────────────────────────────────────

    [Fact]
    public void GetFallbackChain_QualityFirst_SortedByQualityDesc()
    {
        var providers = new List<ProviderConfig>
        {
            Make("low",  qualityScore: 30),
            Make("high", qualityScore: 90),
            Make("mid",  qualityScore: 60),
        };
        var result = _router.GetFallbackChain(providers, ProviderRoutingStrategy.QualityFirst);
        Assert.Equal(["high", "mid", "low"], result.Select(p => p.Id).ToArray());
    }

    [Fact]
    public void GetFallbackChain_QualityFirst_EqualScore_DefaultFirst()
    {
        var providers = new List<ProviderConfig>
        {
            Make("a", qualityScore: 80),
            Make("b", qualityScore: 80, isDefault: true),
        };
        var result = _router.GetFallbackChain(providers, ProviderRoutingStrategy.QualityFirst);
        Assert.Equal("b", result[0].Id);
    }

    // ── CostFirst 策略 ──────────────────────────────────────────────────────

    [Fact]
    public void GetFallbackChain_CostFirst_SortedByCostAsc()
    {
        var providers = new List<ProviderConfig>
        {
            Make("expensive", inputPrice: 10m, outputPrice: 20m),
            Make("cheap",     inputPrice: 1m,  outputPrice: 2m),
            Make("mid",       inputPrice: 5m,  outputPrice: 10m),
        };
        var result = _router.GetFallbackChain(providers, ProviderRoutingStrategy.CostFirst);
        Assert.Equal(["cheap", "mid", "expensive"], result.Select(p => p.Id).ToArray());
    }

    [Fact]
    public void GetFallbackChain_CostFirst_NullPriceTreatedAsZero()
    {
        var providers = new List<ProviderConfig>
        {
            Make("paid",  inputPrice: 5m, outputPrice: 5m),
            Make("free"),  // null price → 0
        };
        var result = _router.GetFallbackChain(providers, ProviderRoutingStrategy.CostFirst);
        Assert.Equal("free", result[0].Id);
        Assert.Equal("paid", result[1].Id);
    }

    // ── LatencyFirst 策略 ───────────────────────────────────────────────────

    [Fact]
    public void GetFallbackChain_LatencyFirst_SortedByLatencyAsc()
    {
        var providers = new List<ProviderConfig>
        {
            Make("slow",   latency: LatencyTier.High),
            Make("fast",   latency: LatencyTier.Low),
            Make("medium", latency: LatencyTier.Medium),
        };
        var result = _router.GetFallbackChain(providers, ProviderRoutingStrategy.LatencyFirst);
        Assert.Equal(["fast", "medium", "slow"], result.Select(p => p.Id).ToArray());
    }

    [Fact]
    public void GetFallbackChain_LatencyFirst_EqualTier_DefaultFirst()
    {
        var providers = new List<ProviderConfig>
        {
            Make("a", latency: LatencyTier.Low),
            Make("b", latency: LatencyTier.Low, isDefault: true),
        };
        var result = _router.GetFallbackChain(providers, ProviderRoutingStrategy.LatencyFirst);
        Assert.Equal("b", result[0].Id);
    }

    // ── Route() 与 GetFallbackChain() 的一致性 ──────────────────────────────

    [Fact]
    public void Route_ReturnsFirstOfFallbackChain()
    {
        var providers = new List<ProviderConfig>
        {
            Make("a", qualityScore: 30),
            Make("b", qualityScore: 90),
        };
        ProviderConfig? routed = _router.Route(providers, ProviderRoutingStrategy.QualityFirst);
        IReadOnlyList<ProviderConfig> chain = _router.GetFallbackChain(providers, ProviderRoutingStrategy.QualityFirst);
        Assert.NotNull(routed);
        Assert.Equal(chain[0].Id, routed!.Id);
    }

    [Fact]
    public void Route_OnEmptyList_ReturnsNull_And_ChainReturnsEmpty()
    {
        Assert.Null(_router.Route([], ProviderRoutingStrategy.Default));
        Assert.Empty(_router.GetFallbackChain([], ProviderRoutingStrategy.Default));
    }
}
