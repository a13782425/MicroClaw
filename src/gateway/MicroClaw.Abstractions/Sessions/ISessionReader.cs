namespace MicroClaw.Abstractions.Sessions;

/// <summary>
/// 供 AgentRunner 读取 Session 元数据（如 ProviderId）的轻量接口。
/// 避免 MicroClaw.Agent 项目直接引用 MicroClaw 主项目造成循环依赖。
/// </summary>
/// <remarks>请迁移到 <see cref="ISessionRepository"/>，此接口将在后续版本中移除。</remarks>
[Obsolete("Use ISessionRepository instead. This interface will be removed in a future version.")]
public interface ISessionReader
{
    /// <summary>获取指定会话的元数据，不存在时返回 null。</summary>
    SessionInfo? Get(string id);
}
