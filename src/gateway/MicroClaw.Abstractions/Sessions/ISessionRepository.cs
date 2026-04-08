namespace MicroClaw.Abstractions.Sessions;

/// <summary>
/// Session 聚合根的完整仓储接口（CRUD + 消息操作）。
/// </summary>
public interface ISessionRepository
{
    // ── 查询 ──────────────────────────────────────────────────────────────────

    /// <summary>按 ID 获取 Session 只读视图。不存在时返回 null。</summary>
    IMicroSession? Get(string id);

    /// <summary>获取所有会话的快照。</summary>
    IReadOnlyList<IMicroSession> GetAll();

    /// <summary>仅返回顶层会话（ParentSessionId 为 null）。</summary>
    IReadOnlyList<IMicroSession> GetTopLevel();

    /// <summary>
    /// 沿 ParentSessionId 链向上遍历，返回根会话 ID。
    /// 若 <paramref name="sessionId"/> 本身即为根会话，则直接返回它。
    /// </summary>
    string GetRootSessionId(string sessionId);

    // ── 命令 ──────────────────────────────────────────────────────────────────

    /// <summary>持久化 Session 状态（新增或更新）。</summary>
    void Save(IMicroSession microSession);

    /// <summary>删除指定会话及其历史消息目录。返回 false 表示会话不存在。</summary>
    bool Delete(string id);

    // ── 消息操作 ──────────────────────────────────────────────────────────────

    /// <summary>追加一条消息到指定会话的消息历史。</summary>
    void AddMessage(string sessionId, SessionMessage message);

    /// <summary>获取指定会话的全量消息历史。</summary>
    IReadOnlyList<SessionMessage> GetMessages(string sessionId);

    /// <summary>分页获取消息历史（从末尾倒序）。</summary>
    (IReadOnlyList<SessionMessage> Messages, int Total) GetMessagesPaged(
        string sessionId, int skip, int limit);

    /// <summary>从消息历史中移除指定 ID 的消息（用于上下文溢出清理）。</summary>
    void RemoveMessages(string sessionId, IReadOnlySet<string> messageIds);
}
