using MicroClaw.Tools;

namespace MicroClaw.Pet.Decision;

/// <summary>
/// Pet 消息调度决策结果。由 PetDecisionEngine 通过 LLM 生成。
/// 描述一条用户消息应该如何被处理：委派给哪个 Agent、使用哪个 Provider、
/// 启用/禁用哪些工具、是否注入 Pet 私有知识。
/// </summary>
public sealed record PetDispatchResult
{
    /// <summary>
    /// 委派的 Agent ID。null 表示使用 Session 默认绑定的 Agent。
    /// </summary>
    public string? AgentId { get; init; }

    /// <summary>
    /// 指定使用的 Provider ID。null 表示使用默认路由策略。
    /// </summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// 工具分组覆盖配置。非空时覆盖 Agent 的默认工具配置。
    /// 空列表表示不覆盖（使用 Agent 默认配置）。
    /// </summary>
    public IReadOnlyList<ToolGroupConfig> ToolOverrides { get; init; } = [];

    /// <summary>
    /// 从 Pet 私有 RAG 检索到的知识，注入到 Agent 的上下文中。
    /// null 表示无相关知识。
    /// </summary>
    public string? PetKnowledge { get; init; }

    /// <summary>
    /// 是否由 Pet 自己直接回复用户（不委派 Agent）。
    /// 例如用户询问 Pet 状态、闲聊等场景。
    /// </summary>
    public bool ShouldPetRespond { get; init; }

    /// <summary>
    /// Pet 自己的回复内容。仅当 <see cref="ShouldPetRespond"/> 为 <c>true</c> 时使用。
    /// </summary>
    public string? PetResponse { get; init; }

    /// <summary>
    /// 决策原因说明（供调试/日志使用）。
    /// </summary>
    public string Reason { get; init; } = string.Empty;
}
