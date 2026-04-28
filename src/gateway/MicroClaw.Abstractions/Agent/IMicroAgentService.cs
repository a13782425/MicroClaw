namespace MicroClaw.Abstractions.Agent;
/// <summary>
/// Agent 运行时管理服务：管理全部 IMicroAgent 实例的生命周期，
/// 并暴露查询入口供 MicroPet / SubAgentRunner / WorkflowEngine 使用。
/// 实现类为 MicroClaw.Agent.AgentService（: MicroService），替代 AgentRunner + AgentStore 的执行职责。
/// </summary>
public interface IMicroAgentService
{
    IReadOnlyList<IMicroAgent> All { get; }
    
    /// <summary>返回 IsDefault=true 的 Agent，不存在时返回 null。</summary>
    IMicroAgent? GetDefault();
    
    /// <summary>按 Id 查找 Agent，不存在时返回 null。</summary>
    IMicroAgent? GetById(string id);
}