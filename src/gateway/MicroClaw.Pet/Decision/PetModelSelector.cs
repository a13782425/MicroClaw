using MicroClaw.Providers;

namespace MicroClaw.Pet.Decision;

/// <summary>
/// Pet 动态模型选择器：按场景选择 Provider 路由策略，并可选地使用首选 Provider。
/// <para>
/// 路由规则：
/// <list type="bullet">
///   <item><b>心跳状态评估</b>（Heartbeat）→ <see cref="ProviderRoutingStrategy.CostFirst"/>：心跳高频低价值，用最便宜的模型。</item>
///   <item><b>消息调度</b>（Dispatch）→ <see cref="ProviderRoutingStrategy.Default"/>：常规消息使用默认 Provider。</item>
///   <item><b>学习 / 反思</b>（Learning / Reflecting）→ <see cref="ProviderRoutingStrategy.QualityFirst"/>：深度思考需要高质量模型。</item>
///   <item>有 <c>PreferredProviderId</c> 时 → 优先使用指定 Provider，不命中再回退路由。</item>
/// </list>
/// </para>
/// </summary>
public sealed class PetModelSelector(
    ProviderService providerService,
    IProviderRouter providerRouter)
{
    private readonly ProviderService _providerService = providerService ?? throw new ArgumentNullException(nameof(providerService));
    private readonly IProviderRouter _providerRouter = providerRouter ?? throw new ArgumentNullException(nameof(providerRouter));

    /// <summary>
    /// 根据场景和可选的首选 Provider 选择最合适的 Provider。
    /// </summary>
    /// <param name="scenario">当前使用场景。</param>
    /// <param name="preferredProviderId">首选 Provider ID（来自 PetConfig）。null 表示无偏好。</param>
    /// <returns>选中的 Provider 配置；无可用 Provider 时返回 <c>null</c>。</returns>
    public ProviderConfig? Select(PetModelScenario scenario, string? preferredProviderId = null)
    {
        var allProviders = _providerService.All;

        // 优先使用首选 Provider（若指定且可用）
        if (!string.IsNullOrWhiteSpace(preferredProviderId))
        {
            var preferred = allProviders.FirstOrDefault(p =>
                p.Id == preferredProviderId &&
                p.IsEnabled &&
                p.ModelType != ModelType.Embedding);

            if (preferred is not null)
                return preferred;
        }

        // 按场景选择路由策略
        var strategy = MapScenarioToStrategy(scenario);
        return _providerRouter.Route(allProviders, strategy);
    }

    /// <summary>
    /// 根据场景返回按策略排序的 Provider 回退链。
    /// </summary>
    /// <param name="scenario">当前使用场景。</param>
    /// <param name="preferredProviderId">首选 Provider ID。</param>
    /// <returns>排序后的 Provider 列表，第一个为最优选择。</returns>
    public IReadOnlyList<ProviderConfig> GetFallbackChain(PetModelScenario scenario, string? preferredProviderId = null)
    {
        var allProviders = _providerService.All;
        var strategy = MapScenarioToStrategy(scenario);
        var chain = _providerRouter.GetFallbackChain(allProviders, strategy).ToList();

        // 若有首选 Provider 且在链中，将其提升到第一位
        if (!string.IsNullOrWhiteSpace(preferredProviderId))
        {
            int idx = chain.FindIndex(p => p.Id == preferredProviderId);
            if (idx > 0)
            {
                var preferred = chain[idx];
                chain.RemoveAt(idx);
                chain.Insert(0, preferred);
            }
        }

        return chain.AsReadOnly();
    }

    /// <summary>
    /// 将使用场景映射为 Provider 路由策略。
    /// </summary>
    internal static ProviderRoutingStrategy MapScenarioToStrategy(PetModelScenario scenario) =>
        scenario switch
        {
            PetModelScenario.Heartbeat => ProviderRoutingStrategy.CostFirst,
            PetModelScenario.Learning => ProviderRoutingStrategy.QualityFirst,
            PetModelScenario.Reflecting => ProviderRoutingStrategy.QualityFirst,
            PetModelScenario.PromptEvolution => ProviderRoutingStrategy.QualityFirst,
            PetModelScenario.Dispatch => ProviderRoutingStrategy.Default,
            _ => ProviderRoutingStrategy.Default,
        };
}

/// <summary>
/// Pet 使用 LLM 的场景分类，用于决定路由策略。
/// </summary>
public enum PetModelScenario
{
    /// <summary>心跳状态评估：高频低价值，用最便宜的模型。</summary>
    Heartbeat,

    /// <summary>消息调度决策：常规消息，使用默认 Provider。</summary>
    Dispatch,

    /// <summary>学习：深度分析内容，需要高质量模型。</summary>
    Learning,

    /// <summary>反思：回顾会话、生成洞察，需要高质量模型。</summary>
    Reflecting,

    /// <summary>提示词进化：修改 Pet 自身提示词，需要高质量模型。</summary>
    PromptEvolution,
}
