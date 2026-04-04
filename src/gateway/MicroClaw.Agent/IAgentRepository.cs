namespace MicroClaw.Agent;

/// <summary>
/// Agent 仓储接口：提供 Agent 实体的 CRUD 操作。
/// AgentStore 是其主要实现。
/// </summary>
public interface IAgentRepository
{
    /// <summary>返回所有 Agent。</summary>
    IReadOnlyList<Agent> GetAll();

    /// <summary>按 ID 查找 Agent，不存在时返回 null。</summary>
    Agent? GetById(string id);

    /// <summary>返回 IsDefault=true 的 Agent，不存在时返回 null。</summary>
    Agent? GetDefault();

    /// <summary>按名称查找已启用的 Agent，不存在时返回 null。</summary>
    Agent? GetByName(string name);

    /// <summary>
    /// 保存（upsert）Agent：Id 为空时创建新 Agent（自动分配 Id），否则更新已有 Agent。
    /// 返回保存后的 Agent（含已分配的 Id）。
    /// </summary>
    Agent Save(Agent agent);

    /// <summary>删除 Agent。默认代理（IsDefault=true）不可删除，返回 false。</summary>
    bool Delete(string id);
}
