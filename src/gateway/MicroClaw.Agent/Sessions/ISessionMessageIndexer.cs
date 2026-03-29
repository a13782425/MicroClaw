using MicroClaw.Gateway.Contracts.Sessions;

namespace MicroClaw.Agent.Sessions;

/// <summary>
/// 会话消息 RAG 索引器：对话完成后将新消息向量化写入 Session RAG DB。
/// </summary>
public interface ISessionMessageIndexer
{
    /// <summary>
    /// 扫描 <paramref name="messages"/> 中尚未索引的 user/assistant 消息，
    /// 向量化后写入指定会话的 Session RAG DB（增量、幂等）。
    /// </summary>
    Task IndexNewMessagesAsync(
        string sessionId,
        IReadOnlyList<SessionMessage> messages,
        CancellationToken ct = default);
}
