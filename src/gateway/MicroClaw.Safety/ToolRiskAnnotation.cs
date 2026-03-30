namespace MicroClaw.Safety;

/// <summary>
/// 描述单个工具的风险标注条目。
/// </summary>
/// <param name="ToolName">工具名称（与 <see cref="Microsoft.Extensions.AI.AIFunction.Name"/> 对应）。</param>
/// <param name="RiskLevel">该工具的风险等级。</param>
/// <param name="Rationale">风险等级判定说明，用于审计与 UI 展示。</param>
public sealed record ToolRiskAnnotation(
    string ToolName,
    RiskLevel RiskLevel,
    string Rationale);
