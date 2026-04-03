namespace MicroClaw.Pet;

/// <summary>
/// Pet 的行为状态枚举，由 LLM 驱动的状态机决定。
/// </summary>
public enum PetBehaviorState
{
    /// <summary>空闲状态（默认）：等待消息，无自主活动。</summary>
    Idle,

    /// <summary>学习状态：正在从会话内容或外部资源中学习，写入私有 RAG。</summary>
    Learning,

    /// <summary>整理状态：整理记忆、归纳知识、清理冗余内容。</summary>
    Organizing,

    /// <summary>休息状态：低活跃，减少 LLM 调用，仅响应必要消息。</summary>
    Resting,

    /// <summary>反思状态：深度回顾会话历史，生成洞察。</summary>
    Reflecting,

    /// <summary>社交状态：主动与用户互动，分享想法或观察。</summary>
    Social,

    /// <summary>
    /// Panic 状态：检测到异常（多次失败/速率超限），暂停自主行为，等待恢复。
    /// </summary>
    Panic,

    /// <summary>调度状态：正在处理用户消息，委派 Agent 执行。</summary>
    Dispatching,
}
