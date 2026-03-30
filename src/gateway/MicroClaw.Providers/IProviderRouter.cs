namespace MicroClaw.Providers;

/// <summary>
/// Provider 路由器：根据路由策略从候选 Provider 列表中选择最合适的一个，或返回按优先级排序的完整回退链。
/// </summary>
public interface IProviderRouter
{
    /// <summary>
    /// 从 <paramref name="candidates"/> 中按 <paramref name="strategy"/> 选择一个已启用的 Provider。
    /// </summary>
    /// <param name="candidates">全部 Provider 配置（含已禁用的）。</param>
    /// <param name="strategy">路由策略。</param>
    /// <returns>选中的 <see cref="ProviderConfig"/>；若无可用 Provider 则返回 <c>null</c>。</returns>
    ProviderConfig? Route(IReadOnlyList<ProviderConfig> candidates, ProviderRoutingStrategy strategy);

    /// <summary>
    /// 返回按 <paramref name="strategy"/> 排序的已启用 Provider 完整列表，供自动回退使用。
    /// 第一个元素是最优选择，后续为依次备用。
    /// </summary>
    /// <param name="candidates">全部 Provider 配置（含已禁用的）。</param>
    /// <param name="strategy">路由策略。</param>
    /// <returns>按策略排好序的已启用 Provider 列表；若无可用 Provider 则返回空列表。</returns>
    IReadOnlyList<ProviderConfig> GetFallbackChain(IReadOnlyList<ProviderConfig> candidates, ProviderRoutingStrategy strategy);
}
