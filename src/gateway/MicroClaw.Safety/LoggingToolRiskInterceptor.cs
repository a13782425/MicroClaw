using Microsoft.Extensions.Logging;

namespace MicroClaw.Safety;

/// <summary>
/// 基于日志的工具风险拦截器（默认实现）。
/// 根据风险等级以不同日志级别记录工具调用信息，但默认放行所有调用。
/// 实际阻止行为由 2-C-4（白名单/灰名单配置）提供的替换实现负责。
/// </summary>
public sealed class LoggingToolRiskInterceptor(ILogger<LoggingToolRiskInterceptor> logger)
    : IToolRiskInterceptor
{
    private readonly ILogger<LoggingToolRiskInterceptor> _logger =
        logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    public Task<ToolInterceptResult> InterceptAsync(
        string toolName,
        RiskLevel riskLevel,
        IDictionary<string, object?>? args,
        CancellationToken ct = default)
    {
        switch (riskLevel)
        {
            case RiskLevel.Low:
                // 低风险：不产生日志，直接放行
                break;

            case RiskLevel.Medium:
                _logger.LogInformation(
                    "[RiskCheck] Tool={ToolName} RiskLevel={RiskLevel} — 允许执行（中风险）",
                    toolName, riskLevel);
                break;

            case RiskLevel.High:
                _logger.LogWarning(
                    "[RiskCheck] Tool={ToolName} RiskLevel={RiskLevel} — 允许执行（高风险），请关注操作影响",
                    toolName, riskLevel);
                break;

            case RiskLevel.Critical:
                _logger.LogWarning(
                    "[RiskCheck] Tool={ToolName} RiskLevel={RiskLevel} — 允许执行（严重风险），如需阻止请配置白名单/灰名单（2-C-4）",
                    toolName, riskLevel);
                break;
        }

        return Task.FromResult(ToolInterceptResult.Allow());
    }
}
