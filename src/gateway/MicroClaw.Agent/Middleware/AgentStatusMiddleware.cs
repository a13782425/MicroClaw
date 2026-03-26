using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MicroClaw.Agent.Middleware;

/// <summary>
/// Agent 运行状态中间件（AF Agent Run 层）。
/// 提供创建 agent-level 中间件委托的工厂方法，用于在 agent 运行开始/结束时发送 running/completed/failed 通知。
/// <para>Phase 1 中该逻辑由 <see cref="AgentRunner"/> 直接调用，本文件是从 AgentRunner 提取的参考实现。</para>
/// </summary>
public static class AgentStatusMiddleware
{
    /// <summary>
    /// 创建状态通知中间件，在 Agent 开始运行时发送 "running"，成功后发送 "completed"，异常时发送 "failed"。
    /// </summary>
    public static Func<
        IEnumerable<ChatMessage>, AgentSession, AgentRunOptions,
        Func<IEnumerable<ChatMessage>, AgentSession, AgentRunOptions, CancellationToken, Task>,
        CancellationToken, Task>
        Create(IAgentStatusNotifier notifier, string sessionId, string agentId)
    {
        return async (messages, session, options, next, ct) =>
        {
            if (!string.IsNullOrWhiteSpace(sessionId))
                await notifier.NotifyAsync(sessionId, agentId, "running", ct);

            bool succeeded = false;
            try
            {
                await next(messages, session, options, ct);
                succeeded = true;
            }
            finally
            {
                if (!string.IsNullOrWhiteSpace(sessionId))
                    await notifier.NotifyAsync(sessionId, agentId, succeeded ? "completed" : "failed", CancellationToken.None);
            }
        };
    }
}
