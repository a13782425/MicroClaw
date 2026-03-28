using MicroClaw.Gateway.Contracts;

namespace MicroClaw.Tools;

/// <summary>
/// 工具创建上下文 — 在 <see cref="IToolProvider.CreateToolsAsync"/> 中传递运行时信息。
/// Provider 按需使用所需字段，不适用的字段忽略即可。
/// </summary>
public sealed record ToolCreationContext(
    /// <summary>当前会话 ID。需要会话上下文的工具在此为 null 时返回空列表。</summary>
    string? SessionId = null,
    /// <summary>当前会话的渠道类型。渠道工具 Provider 据此判断是否适用。</summary>
    ChannelType? ChannelType = null,
    /// <summary>当前会话的渠道配置 ID。渠道工具 Provider 据此加载凭据等配置。</summary>
    string? ChannelId = null,
    /// <summary>Agent 禁用的技能 ID 排除列表。空列表 = 全部启用（opt-out 模型）。</summary>
    IReadOnlyList<string>? DisabledSkillIds = null,
    /// <summary>当前调用代理 ID。用于子代理工具排除自身。</summary>
    string? CallingAgentId = null,
    /// <summary>
    /// 允许调用的子代理 ID 白名单。
    /// null = 允许调用所有子代理；空列表 = 禁止调用任何子代理；具体 ID 列表 = 仅允许指定子代理。
    /// </summary>
    IReadOnlyList<string>? AllowedSubAgentIds = null,
    /// <summary>祖先链代理 ID 列表。用于防止子代理循环调用（排除整条调用链上的代理）。</summary>
    IReadOnlyList<string>? AncestorAgentIds = null);
