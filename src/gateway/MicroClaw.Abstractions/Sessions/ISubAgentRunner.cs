namespace MicroClaw.Abstractions.Sessions;

/// <summary>
/// 允许 Agent 工具层启动一次性子 Agent 执行任务（类似 Claude Code 子任务机制）。
/// 由主项目实现，通过 DI 注入至 AgentRunner，避免 MicroClaw.Agent 直接引用主项目造成循环依赖。
/// </summary>
public interface ISubAgentRunner
{
    /// <summary>
    /// 启动子 Agent 执行指定任务。
    /// 执行过程依附于根会话记录，但不会创建或持久化独立子会话。
    /// </summary>
    Task<string> RunSubAgentAsync(
        string agentId,
        string task,
        string parentSessionId,
        CancellationToken ct = default);
}
