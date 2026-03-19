namespace MicroClaw.Gateway.Contracts.Sessions;

/// <summary>
/// 渠道消息处理器通过此接口将消息路由到 Agent（ReAct 循环）执行。
/// 由 MicroClaw.Agent 项目实现，通过 DI 注入到渠道处理器。
/// </summary>
public interface IAgentMessageHandler
{
    /// <summary>检查指定渠道是否有启用的 Agent 绑定。</summary>
    bool HasAgentForChannel(string channelId);

    /// <summary>将消息路由到 Agent 执行 ReAct 循环，返回 AI 回复文本。</summary>
    Task<string> HandleMessageAsync(
        string channelId,
        string sessionId,
        IReadOnlyList<SessionMessage> history,
        CancellationToken ct = default);
}
