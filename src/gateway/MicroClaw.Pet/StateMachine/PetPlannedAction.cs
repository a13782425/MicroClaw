namespace MicroClaw.Pet.StateMachine;

/// <summary>
/// Pet 状态机决策输出的计划动作。由 LLM 决定执行哪些自主行为。
/// </summary>
public sealed record PetPlannedAction
{
    /// <summary>动作类型。</summary>
    public required PetActionType Type { get; init; }

    /// <summary>动作参数（可选），含义随 <see cref="Type"/> 变化。</summary>
    public string? Parameter { get; init; }

    /// <summary>动作原因说明。</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Pet 可执行的自主动作类型。
/// </summary>
public enum PetActionType
{
    /// <summary>从网页获取内容（Parameter = URL）。</summary>
    FetchWeb,

    /// <summary>将内容摘要写入 Pet 私有 RAG 记忆。</summary>
    SummarizeToMemory,

    /// <summary>整理 Pet 私有 RAG 记忆（合并/去重/归类）。</summary>
    OrganizeMemory,

    /// <summary>反思会话历史，生成洞察。</summary>
    ReflectOnSession,

    /// <summary>触发提示词进化（personality/dispatch-rules/knowledge-interests）。</summary>
    EvolvePrompts,

    /// <summary>向用户发送通知消息（Parameter = 消息内容）。</summary>
    NotifyUser,

    /// <summary>委派任务给指定 Agent 执行（Parameter = AgentId）。</summary>
    DelegateToAgent,
}
