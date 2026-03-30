namespace MicroClaw.Emotion;

/// <summary>
/// 可触发情绪变化的事件类型。
/// </summary>
public enum EmotionEventType
{
    /// <summary>消息发送成功（LLM 正常响应）。</summary>
    MessageSuccess,

    /// <summary>消息发送失败（LLM 调用异常或超时）。</summary>
    MessageFailed,

    /// <summary>Tool 执行成功，返回有效结果。</summary>
    ToolSuccess,

    /// <summary>Tool 执行报错（异常或断言失败）。</summary>
    ToolError,

    /// <summary>用户表达满意（点赞、正面反馈等）。</summary>
    UserSatisfied,

    /// <summary>用户表达不满意（点踩、投诉、负面反馈等）。</summary>
    UserDissatisfied,

    /// <summary>整体任务/工作流成功完成。</summary>
    TaskCompleted,

    /// <summary>整体任务/工作流失败（超过重试上限、显式中止等）。</summary>
    TaskFailed,

    /// <summary>记录了一条高严重度（High）痛觉记忆。触发谨慎情绪偏移。</summary>
    PainOccurredHigh,

    /// <summary>记录了一条极高严重度（Critical）痛觉记忆。触发强烈谨慎情绪偏移。</summary>
    PainOccurredCritical,
}
