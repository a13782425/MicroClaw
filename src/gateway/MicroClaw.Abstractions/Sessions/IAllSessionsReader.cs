namespace MicroClaw.Abstractions.Sessions;

/// <summary>
/// 提供所有会话列表的只读接口，用于跨模块查询（例如通过 AgentId 查找关联 Session）。
/// 设计目的：避免依赖 SessionStore 具体类，保持模块间解耦。
/// </summary>
public interface IAllSessionsReader
{
    /// <summary>获取所有会话的快照列表（包含已禁用、未审批的会话）。</summary>
    IReadOnlyList<SessionInfo> GetAll();
}
