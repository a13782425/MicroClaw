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
}
