namespace MicroClaw.Abstractions.Sessions;

/// <summary>
/// 允许 Agent 工具层启动子 Agent 会话执行任务（类似 Claude Code 子任务机制）。
/// 由主项目实现，通过 DI 注入至 AgentRunner，避免 MicroClaw.Agent 直接引用主项目造成循环依赖。
/// </summary>
public interface ISubAgentRunner
{
    /// <summary>
    /// 启动子 Agent 会话执行指定任务。
    /// 子会话会被持久化（agentId + parentSessionId），执行完成后返回结果文本。
    /// </summary>
    Task<string> RunSubAgentAsync(
        string agentId,
        string task,
        string parentSessionId,
        CancellationToken ct = default);
}
