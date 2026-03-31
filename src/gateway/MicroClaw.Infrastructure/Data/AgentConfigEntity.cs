namespace MicroClaw.Infrastructure.Data;

public sealed class AgentConfigEntity
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? DisabledSkillIdsJson { get; set; }
    public string? DisabledMcpServerIdsJson { get; set; }
    public string? ToolGroupConfigsJson { get; set; }
    /// <summary>创建时间：相对于 TimeBase.BaseTime 的毫秒偏移。</summary>
    public long CreatedAtMs { get; set; }
    public bool IsDefault { get; set; }
    public int? ContextWindowMessages { get; set; }
    public bool ExposeAsA2A { get; set; }
    /// <summary>
    /// 允许调用的子代理 ID 白名单（JSON 数组）。
    /// null = 允许调用所有子代理（默认）；空数组 "[]" = 禁止调用任何子代理；具体 ID 列表 = 仅允许调用指定子代理。
    /// </summary>
    public string? AllowedSubAgentIdsJson { get; set; }
    /// <summary>
    /// Provider 路由策略：Default / QualityFirst / CostFirst / LatencyFirst。
    /// null 等同于 Default（向后兼容历史数据）。
    /// </summary>
    public string? RoutingStrategy { get; set; }

    /// <summary>月度预算上限（USD）。null 表示不限制。超出时记录 Warning 日志。</summary>
    public decimal? MonthlyBudgetUsd { get; set; }

    /// <summary>
    /// Source plugin name (e.g. "plugin:my-plugin"). null = manually created, not from a plugin.
    /// Used by <see cref="MicroClaw.Gateway.Contracts.Plugins.IPluginAgentRegistrar"/> for bulk removal.
    /// </summary>
    public string? SourcePlugin { get; set; }
}
