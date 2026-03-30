using MicroClaw.Providers;

namespace MicroClaw.Tests.Providers;

/// <summary>
/// ProviderRouter 单元测试：覆盖四种路由策略 + 边界条件。
/// </summary>
public sealed class ProviderRouterTests
{
    private readonly ProviderRouter _router = new();

    // ── 测试数据工厂 ────────────────────────────────────────────────────────

    private static ProviderConfig MakeProvider(
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

    // ── Default 策略 ────────────────────────────────────────────────────────

    [Fact]
    public void Route_Default_ReturnsDefaultProvider()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("a"),
            MakeProvider("b", isDefault: true),
            MakeProvider("c"),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.Default);

        Assert.NotNull(result);
        Assert.Equal("b", result.Id);
    }

    [Fact]
    public void Route_Default_WhenNoDefaultFlagReturnsFirstEnabled()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("x"),
            MakeProvider("y"),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.Default);

        Assert.NotNull(result);
        Assert.Equal("x", result.Id);
    }

    [Fact]
    public void Route_Default_IgnoresDisabledProviders()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("disabled", isEnabled: false, isDefault: true),
            MakeProvider("enabled"),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.Default);

        Assert.NotNull(result);
        Assert.Equal("enabled", result.Id);
    }

    [Fact]
    public void Route_Default_EmptyListReturnsNull()
    {
        ProviderConfig? result = _router.Route([], ProviderRoutingStrategy.Default);
        Assert.Null(result);
    }

    [Fact]
    public void Route_Default_AllDisabledReturnsNull()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("a", isEnabled: false),
            MakeProvider("b", isEnabled: false),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.Default);
        Assert.Null(result);
    }

    // ── QualityFirst 策略 ───────────────────────────────────────────────────

    [Fact]
    public void Route_QualityFirst_ReturnsHighestQualityScore()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("low", qualityScore: 30),
            MakeProvider("high", qualityScore: 90),
            MakeProvider("mid", qualityScore: 60),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.QualityFirst);

        Assert.NotNull(result);
        Assert.Equal("high", result.Id);
    }

    [Fact]
    public void Route_QualityFirst_SameScoredPrefersDefault()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("a", qualityScore: 80),
            MakeProvider("b", qualityScore: 80, isDefault: true),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.QualityFirst);

        Assert.NotNull(result);
        Assert.Equal("b", result.Id);
    }

    [Fact]
    public void Route_QualityFirst_IgnoresDisabledProviders()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("best_disabled", isEnabled: false, qualityScore: 99),
            MakeProvider("second", qualityScore: 70),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.QualityFirst);

        Assert.NotNull(result);
        Assert.Equal("second", result.Id);
    }

    [Fact]
    public void Route_QualityFirst_DefaultQualityScore50IsUsed()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("a"),            // default qualityScore = 50
            MakeProvider("b", qualityScore: 49),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.QualityFirst);

        Assert.NotNull(result);
        Assert.Equal("a", result.Id);
    }

    // ── CostFirst 策略 ──────────────────────────────────────────────────────

    [Fact]
    public void Route_CostFirst_ReturnsLowestTotalCost()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("expensive", inputPrice: 10m, outputPrice: 30m),
            MakeProvider("cheap", inputPrice: 0.1m, outputPrice: 0.4m),
            MakeProvider("mid", inputPrice: 2m, outputPrice: 6m),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.CostFirst);

        Assert.NotNull(result);
        Assert.Equal("cheap", result.Id);
    }

    [Fact]
    public void Route_CostFirst_NullPricesTreatedAsZero()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("free", inputPrice: null, outputPrice: null),
            MakeProvider("cheap", inputPrice: 0.01m, outputPrice: 0.01m),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.CostFirst);

        // 两者不相等时 free（0）应赢
        Assert.NotNull(result);
        Assert.Equal("free", result.Id);
    }

    [Fact]
    public void Route_CostFirst_SameCostPrefersDefault()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("a", inputPrice: 1m, outputPrice: 2m),
            MakeProvider("b", inputPrice: 1m, outputPrice: 2m, isDefault: true),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.CostFirst);

        Assert.NotNull(result);
        Assert.Equal("b", result.Id);
    }

    [Fact]
    public void Route_CostFirst_IgnoresDisabledProviders()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("free_disabled", isEnabled: false, inputPrice: null, outputPrice: null),
            MakeProvider("paid", inputPrice: 1m, outputPrice: 2m),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.CostFirst);

        Assert.NotNull(result);
        Assert.Equal("paid", result.Id);
    }

    [Fact]
    public void Route_CostFirst_OnlyOutputPriceConsideredWhenInputNull()
    {
        // 只有输出价格时也能正确比较
        var providers = new List<ProviderConfig>
        {
            MakeProvider("a", inputPrice: null, outputPrice: 5m),
            MakeProvider("b", inputPrice: null, outputPrice: 1m),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.CostFirst);

        Assert.NotNull(result);
        Assert.Equal("b", result.Id);
    }

    // ── LatencyFirst 策略 ───────────────────────────────────────────────────

    [Fact]
    public void Route_LatencyFirst_ReturnsLowestLatencyTier()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("slow", latency: LatencyTier.High),
            MakeProvider("fast", latency: LatencyTier.Low),
            MakeProvider("mid", latency: LatencyTier.Medium),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.LatencyFirst);

        Assert.NotNull(result);
        Assert.Equal("fast", result.Id);
    }

    [Fact]
    public void Route_LatencyFirst_SameTierPrefersDefault()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("a", latency: LatencyTier.Low),
            MakeProvider("b", latency: LatencyTier.Low, isDefault: true),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.LatencyFirst);

        Assert.NotNull(result);
        Assert.Equal("b", result.Id);
    }

    [Fact]
    public void Route_LatencyFirst_DefaultTierMediumIsUsed()
    {
        // 默认 LatencyTier = Medium (1)
        var providers = new List<ProviderConfig>
        {
            MakeProvider("a"),                               // Medium
            MakeProvider("b", latency: LatencyTier.High),   // High
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.LatencyFirst);

        Assert.NotNull(result);
        Assert.Equal("a", result.Id);
    }

    [Fact]
    public void Route_LatencyFirst_IgnoresDisabledProviders()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("fast_disabled", isEnabled: false, latency: LatencyTier.Low),
            MakeProvider("medium", latency: LatencyTier.Medium),
        };

        ProviderConfig? result = _router.Route(providers, ProviderRoutingStrategy.LatencyFirst);

        Assert.NotNull(result);
        Assert.Equal("medium", result.Id);
    }

    // ── 边界条件 ────────────────────────────────────────────────────────────

    [Fact]
    public void Route_SingleProvider_ReturnsItRegardlessOfStrategy()
    {
        var providers = new List<ProviderConfig> { MakeProvider("only") };

        foreach (ProviderRoutingStrategy strategy in Enum.GetValues<ProviderRoutingStrategy>())
        {
            ProviderConfig? result = _router.Route(providers, strategy);
            Assert.NotNull(result);
            Assert.Equal("only", result.Id);
        }
    }

    [Fact]
    public void Route_AllStrategies_ReturnNullWhenAllDisabled()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("a", isEnabled: false),
            MakeProvider("b", isEnabled: false),
        };

        foreach (ProviderRoutingStrategy strategy in Enum.GetValues<ProviderRoutingStrategy>())
        {
            ProviderConfig? result = _router.Route(providers, strategy);
            Assert.Null(result);
        }
    }

    [Fact]
    public void Route_UnknownStrategyFallsBackToDefault()
    {
        var providers = new List<ProviderConfig>
        {
            MakeProvider("a"),
            MakeProvider("default_one", isDefault: true),
        };

        // 传入不在枚举中的值（如强制转换）→ 等同 Default 策略
        ProviderConfig? result = _router.Route(providers, (ProviderRoutingStrategy)999);

        Assert.NotNull(result);
        Assert.Equal("default_one", result.Id);
    }
}
