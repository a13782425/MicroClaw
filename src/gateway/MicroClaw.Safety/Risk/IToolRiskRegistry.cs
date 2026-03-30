namespace MicroClaw.Safety;

/// <summary>
/// 工具风险等级注册表接口。
/// 提供按工具名查询风险等级及列出全部标注的能力。
/// 未知工具返回 <see cref="RiskLevel.Low"/>（宽松默认策略，阻止行为由 <see cref="IToolRiskInterceptor"/> 决定）。
/// </summary>
public interface IToolRiskRegistry
{
    /// <summary>
    /// 返回指定工具名的风险等级。
    /// 未知工具名返回 <see cref="RiskLevel.Low"/>。
    /// </summary>
    RiskLevel GetRiskLevel(string toolName);

    /// <summary>返回注册表中已知的全部风险标注列表。</summary>
    IReadOnlyList<ToolRiskAnnotation> GetAllAnnotations();
}
