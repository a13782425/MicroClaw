namespace MicroClaw.Safety;

/// <summary>
/// 工具风险前置拦截器接口。
/// 在工具被实际调用前，由 AgentFactory 调用以决定是否允许执行。
/// </summary>
public interface IToolRiskInterceptor
{
    /// <summary>
    /// 在工具调用前执行风险检查。
    /// </summary>
    /// <param name="toolName">即将被调用的工具名称。</param>
    /// <param name="riskLevel">该工具的风险等级（由 <see cref="IToolRiskRegistry"/> 提供）。</param>
    /// <param name="args">工具调用参数（可为 null）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>决定是否允许本次调用的 <see cref="ToolInterceptResult"/>。</returns>
    Task<ToolInterceptResult> InterceptAsync(
        string toolName,
        RiskLevel riskLevel,
        IDictionary<string, object?>? args,
        CancellationToken ct = default);
}
