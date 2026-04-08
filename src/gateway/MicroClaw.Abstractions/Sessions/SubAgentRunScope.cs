namespace MicroClaw.Abstractions.Sessions;

/// <summary>
/// 通过 <see cref="AsyncLocal{T}"/> 维护当前子代理执行链，仅用于进程内一次性 SubAgentRun。
/// </summary>
public static class SubAgentRunScope
{
    private static readonly AsyncLocal<SubAgentRunContext?> _current = new();

    /// <summary>获取或设置当前异步上下文中的子代理执行上下文。</summary>
    public static SubAgentRunContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}

/// <summary>
/// 当前子代理执行上下文：根会话 ID 与当前调用链上的祖先代理列表。
/// </summary>
public sealed record SubAgentRunContext(
    string RootSessionId,
    IReadOnlyList<string> AgentChain);
