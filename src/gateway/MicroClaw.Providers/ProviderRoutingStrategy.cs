namespace MicroClaw.Providers;

/// <summary>
/// Provider 路由策略：决定在 Session 未绑定具体 Provider 时如何从已启用的 Provider 中自动选择。
/// </summary>
public enum ProviderRoutingStrategy
{
    /// <summary>使用标记为 <c>IsDefault</c> 的 Provider；若无默认则取第一个已启用的 Provider。</summary>
    Default = 0,

    /// <summary>按 <see cref="ProviderCapabilities.QualityScore"/> 降序选择质量最高的 Provider。</summary>
    QualityFirst = 1,

    /// <summary>
    /// 按总成本（InputPricePerMToken + OutputPricePerMToken）升序选择成本最低的 Provider。
    /// 未标注价格的 Provider 视为免费（成本=0），参与竞选但不一定胜出。
    /// </summary>
    CostFirst = 2,

    /// <summary>按 <see cref="LatencyTier"/> 升序选择响应延迟最低的 Provider。</summary>
    LatencyFirst = 3,
}

/// <summary>
/// Provider 延迟层级，用于 <see cref="ProviderRoutingStrategy.LatencyFirst"/> 路由策略。
/// </summary>
public enum LatencyTier
{
    /// <summary>低延迟：本地小模型或轻量云端模型（如 gpt-4o-mini、gemini-flash）。</summary>
    Low = 0,

    /// <summary>中等延迟：标准云端模型（如 gpt-4o、claude-3-5-sonnet）。</summary>
    Medium = 1,

    /// <summary>高延迟：大上下文或推理型模型（如 o1、claude-3-opus）。</summary>
    High = 2,
}
