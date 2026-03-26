using MicroClaw.Agent.Memory;
using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Agent.ContextProviders;

/// <summary>
/// Session 记忆上下文提供者：将长期记忆 + 每日权重衰减记忆注入 System Prompt。
/// sessionId 为 <c>null</c> 时（如子代理场景）直接返回 <c>null</c> 跳过注入。
/// </summary>
public sealed class SessionMemoryContextProvider(MemoryService memoryService) : IAgentContextProvider
{
    /// <inheritdoc />
    /// <remarks>Order 30：在 Session DNA 之后注入，提供历史记忆上下文。</remarks>
    public int Order => 30;

    /// <inheritdoc />
    public ValueTask<string?> BuildContextAsync(AgentConfig agent, string? sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return ValueTask.FromResult<string?>(null);

        string context = memoryService.BuildMemoryContext(sessionId);
        return ValueTask.FromResult<string?>(string.IsNullOrWhiteSpace(context) ? null : context);
    }
}
