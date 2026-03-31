namespace MicroClaw.Gateway.Contracts.Sessions;

/// <summary>
/// 允许从 Session 持久化历史中移除消息。
/// 由 SessionStore 实现；以轻量接口形式暴露，使 MicroClaw.Agent 无需依赖主项目即可使用。
/// </summary>
public interface ISessionMessageRemover
{
    /// <summary>
    /// 从 messages.jsonl 中移除指定 ID 的消息。未找到的 ID 静默忽略。
    /// </summary>
    void RemoveMessages(string sessionId, IReadOnlySet<string> messageIds);
}
