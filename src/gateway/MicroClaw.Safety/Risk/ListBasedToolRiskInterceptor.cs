using Microsoft.Extensions.Logging;

namespace MicroClaw.Safety;

/// <summary>
/// 基于白名单/灰名单配置的工具风险拦截器。
/// <list type="bullet">
///   <item>
///     <description><b>白名单工具</b>：绕过所有检查，直接放行（不记日志）。</description>
///   </item>
///   <item>
///     <description><b>灰名单工具</b>：阻止执行，返回 [GRAYLIST] 提示，要求 Agent 向用户请求明确确认。</description>
///   </item>
///   <item>
///     <description><b>其他工具</b>：委托给内部的 <see cref="LoggingToolRiskInterceptor"/> 处理（日志 + 放行）。</description>
///   </item>
/// </list>
/// </summary>
public sealed class ListBasedToolRiskInterceptor : IToolRiskInterceptor
{
    private readonly IToolListConfig _listConfig;
    private readonly LoggingToolRiskInterceptor _inner;
    private readonly ILogger<ListBasedToolRiskInterceptor> _logger;

    /// <summary>
    /// 创建基于白名单/灰名单的工具风险拦截器。
    /// </summary>
    /// <param name="listConfig">白名单/灰名单配置。</param>
    /// <param name="innerLogger">委托给内部拦截器使用的日志记录器。</param>
    /// <param name="logger">本拦截器自身的日志记录器。</param>
    public ListBasedToolRiskInterceptor(
        IToolListConfig listConfig,
        ILogger<LoggingToolRiskInterceptor> innerLogger,
        ILogger<ListBasedToolRiskInterceptor> logger)
    {
        _listConfig = listConfig ?? throw new ArgumentNullException(nameof(listConfig));
        _inner = new LoggingToolRiskInterceptor(innerLogger ?? throw new ArgumentNullException(nameof(innerLogger)));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<ToolInterceptResult> InterceptAsync(
        string toolName,
        RiskLevel riskLevel,
        IDictionary<string, object?>? args,
        CancellationToken ct = default)
    {
        // ① 白名单免检：直接放行，不记日志
        if (_listConfig.IsWhitelisted(toolName))
        {
            _logger.LogDebug(
                "[RiskCheck] Tool={ToolName} — 白名单免检，直接放行",
                toolName);
            return Task.FromResult(ToolInterceptResult.Allow());
        }

        // ② 灰名单需确认：阻止执行，提示向用户请求授权
        if (_listConfig.IsGreylisted(toolName))
        {
            string reason = $"工具「{toolName}」位于灰名单（需确认）。请向用户说明此操作的目的和影响，在获得明确授权后方可继续执行。";
            _logger.LogWarning(
                "[RiskCheck] Tool={ToolName} RiskLevel={RiskLevel} — 灰名单拦截，等待用户确认",
                toolName, riskLevel);
            return Task.FromResult(ToolInterceptResult.Block(reason));
        }

        // ③ 其他：委托给默认日志拦截器（按风险等级记日志，始终放行）
        return _inner.InterceptAsync(toolName, riskLevel, args, ct);
    }
}
