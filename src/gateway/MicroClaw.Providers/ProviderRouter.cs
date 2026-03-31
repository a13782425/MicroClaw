namespace MicroClaw.Providers;

/// <summary>
/// 默认 Provider 路由器实现。根据路由策略从已启用的 Provider 中选择最合适的一个，
/// 或返回按优先级排序的完整回退链。
/// </summary>
public sealed class ProviderRouter : IProviderRouter
{
    /// <inheritdoc/>
    public ProviderConfig? Route(IReadOnlyList<ProviderConfig> candidates, ProviderRoutingStrategy strategy)
        => GetFallbackChain(candidates, strategy).FirstOrDefault();

    /// <inheritdoc/>
    public IReadOnlyList<ProviderConfig> GetFallbackChain(
        IReadOnlyList<ProviderConfig> candidates,
        ProviderRoutingStrategy strategy)
    {
        // 只允许 Chat 类型的 Provider 进入回退链，Embedding 模型不能用于对话
        List<ProviderConfig> enabled = candidates
            .Where(p => p.IsEnabled && p.ModelType != ModelType.Embedding)
            .ToList();
        if (enabled.Count == 0)
            return [];

        return strategy switch
        {
            ProviderRoutingStrategy.QualityFirst =>
                enabled
                    .OrderByDescending(p => p.Capabilities.QualityScore)
                    .ThenByDescending(p => p.IsDefault ? 1 : 0)
                    .ToList()
                    .AsReadOnly(),

            ProviderRoutingStrategy.CostFirst =>
                enabled
                    .OrderBy(p =>
                        (p.Capabilities.InputPricePerMToken ?? 0m) +
                        (p.Capabilities.OutputPricePerMToken ?? 0m))
                    .ThenByDescending(p => p.IsDefault ? 1 : 0)
                    .ToList()
                    .AsReadOnly(),

            ProviderRoutingStrategy.LatencyFirst =>
                enabled
                    .OrderBy(p => (int)p.Capabilities.LatencyTier)
                    .ThenByDescending(p => p.IsDefault ? 1 : 0)
                    .ToList()
                    .AsReadOnly(),

            // Default: IsDefault 优先；同层级内保持原始顺序
            _ => enabled
                    .OrderByDescending(p => p.IsDefault ? 1 : 0)
                    .ToList()
                    .AsReadOnly(),
        };
    }
}
