using MicroClaw.Agent.Memory;
using MicroClaw.Infrastructure.Data;

namespace MicroClaw.Agent.ContextProviders;

/// <summary>
/// Session �?DNA 上下文提供者：�?USER.md + AGENTS.md 的内容注�?System Prompt�?
/// sessionId �?<c>null</c> 时（如子代理场景）直接返�?<c>null</c> 跳过注入�?
/// </summary>
public sealed class SessionDnaContextProvider(SessionDnaService sessionDnaService) : IAgentContextProvider
{
    /// <inheritdoc />
    /// <remarks>Order 20：在 Agent DNA 之后注入，提供会话级用户画像和工作流规则�?/remarks>
    public int Order => 20;

    /// <inheritdoc />
    public ValueTask<string?> BuildContextAsync(Agent agent, string? sessionId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return ValueTask.FromResult<string?>(null);

        string context = sessionDnaService.BuildDnaContext(sessionId);
        return ValueTask.FromResult<string?>(string.IsNullOrWhiteSpace(context) ? null : context);
    }
}
