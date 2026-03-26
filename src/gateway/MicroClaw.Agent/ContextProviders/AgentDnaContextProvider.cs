using MicroClaw.Agent.Memory;
using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Agent.ContextProviders;

/// <summary>
/// Agent 级 DNA 上下文提供者：将 SOUL.md + MEMORY.md 的内容注入 System Prompt。
/// 适用于所有 Agent 执行场景（含子代理，无 sessionId 要求）。
/// </summary>
public sealed class AgentDnaContextProvider(AgentDnaService agentDnaService) : IAgentContextProvider
{
    /// <inheritdoc />
    /// <remarks>Order 10：最先注入，作为 System Prompt 的基础人格层。</remarks>
    public int Order => 10;

    /// <inheritdoc />
    public ValueTask<string?> BuildContextAsync(AgentConfig agent, string? sessionId, CancellationToken ct = default)
    {
        string context = agentDnaService.BuildAgentContext(agent.Id);
        return ValueTask.FromResult<string?>(string.IsNullOrWhiteSpace(context) ? null : context);
    }
}
