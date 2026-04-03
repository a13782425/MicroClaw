namespace MicroClaw.Pet;

/// <summary>
/// Pet 的会话级配置模型，控制 Pet 的行为策略与资源限制。
/// </summary>
public sealed class PetConfig
{
    /// <summary>是否启用 Pet 编排层。默认 false（向后兼容）。</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// 活跃时段开始小时（0-23, UTC+0）。Pet 在此时段外不主动发起 LLM 调用。
    /// null 表示全天活跃。
    /// </summary>
    public int? ActiveHoursStart { get; set; }

    /// <summary>
    /// 活跃时段结束小时（0-23, UTC+0）。
    /// null 表示全天活跃。
    /// </summary>
    public int? ActiveHoursEnd { get; set; }

    /// <summary>速率限制：每个窗口内最大 LLM 调用次数。默认 100。</summary>
    public int MaxLlmCallsPerWindow { get; set; } = 100;

    /// <summary>速率限制窗口时长（小时）。默认 5 小时。</summary>
    public double WindowHours { get; set; } = 5.0;

    /// <summary>允许委派的 Agent ID 列表。空列表表示允许所有可用 Agent。</summary>
    public List<string> AllowedAgentIds { get; set; } = [];

    /// <summary>
    /// 首选 Provider ID。Pet 决策引擎在无特殊要求时优先使用此 Provider。
    /// null 表示使用默认路由。
    /// </summary>
    public string? PreferredProviderId { get; set; }

    /// <summary>
    /// 是否启用社交模式（Pet 主动与用户分享想法）。默认 false。
    /// </summary>
    public bool SocialMode { get; set; } = false;
}
