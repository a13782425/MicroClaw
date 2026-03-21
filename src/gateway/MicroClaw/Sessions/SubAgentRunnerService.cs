using MicroClaw.Agent;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;

namespace MicroClaw.Sessions;

/// <summary>
/// 子代理运行服务：创建子会话、调用 AgentRunner 执行 ReAct 循环、持久化对话消息。
/// 实现 ISubAgentRunner 接口，由 MicroClaw.Agent 层通过接口调用，避免循环依赖。
/// 使用 Lazy&lt;AgentRunner&gt; 打破 AgentRunner ↔ SubAgentRunnerService 的循环注册依赖。
/// </summary>
public sealed class SubAgentRunnerService(
    SessionStore sessionStore,
    AgentStore agentStore,
    Lazy<AgentRunner> agentRunnerLazy) : ISubAgentRunner
{
    private const int MaxSubAgentDepth = 3;

    private AgentRunner AgentRunner => agentRunnerLazy.Value;

    public async Task<string> RunSubAgentAsync(
        string agentId,
        string task,
        string parentSessionId,
        CancellationToken ct = default)
    {
        AgentConfig? agent = agentStore.GetById(agentId);
        if (agent is null)
            throw new InvalidOperationException($"子代理 '{agentId}' 不存在。");
        if (!agent.IsEnabled)
            throw new InvalidOperationException($"子代理 '{agent.Name}' 未启用。");

        // 深度检查 + 循环调用检测：沿父会话链向上遍历
        // depth 仅统计具有 ParentSessionId 的祖先会话（即子代理会话），
        // 不计入顶层用户会话，从而允许最多 MaxSubAgentDepth 层子代理嵌套。
        int depth = 0;
        string? cursor = parentSessionId;
        while (cursor is not null)
        {
            SessionInfo? ancestor = sessionStore.Get(cursor);
            if (ancestor is null) break;

            if (ancestor.ParentSessionId is not null)
            {
                // 该祖先本身也是子代理会话，计入深度
                depth++;
                if (depth >= MaxSubAgentDepth)
                    throw new InvalidOperationException(
                        $"子代理调用深度已达上限（{MaxSubAgentDepth}），禁止继续派生子代理。");
            }

            if (ancestor.AgentId == agentId)
                throw new InvalidOperationException(
                    $"检测到循环子代理调用：代理 '{agentId}' 已存在于当前调用链中，禁止循环调用。");

            cursor = ancestor.ParentSessionId;
        }

        // 获取父会话 ProviderId（子会话继承同一模型）
        SessionInfo? parentSession = sessionStore.Get(parentSessionId);
        string providerId = parentSession?.ProviderId ?? string.Empty;

        // 创建子会话（立即批准，无需人工审核）
        string parentShort = parentSessionId.Length > 8 ? parentSessionId[..8] : parentSessionId;
        string title = $"[子代理] {agent.Name} ← {parentShort}";
        SessionInfo subSession = sessionStore.Create(
            title, providerId, ChannelType.Web,
            agentId: agentId,
            parentSessionId: parentSessionId);
        sessionStore.Approve(subSession.Id);

        // 保存用户任务消息
        SessionMessage userMsg = new("user", task, null, DateTimeOffset.UtcNow, null);
        sessionStore.AddMessage(subSession.Id, userMsg);

        // 执行子 Agent ReAct 循环
        string result = await AgentRunner.RunReActAsync(agent, providerId, [userMsg], subSession.Id, ct);

        // 保存 AI 回复，Source 标记来源
        SessionMessage assistantMsg = new("assistant", result, null, DateTimeOffset.UtcNow, null,
            Source: $"sub-agent:{agentId}");
        sessionStore.AddMessage(subSession.Id, assistantMsg);

        return result;
    }
}
