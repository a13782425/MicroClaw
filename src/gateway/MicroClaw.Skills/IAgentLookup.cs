namespace MicroClaw.Skills;

/// <summary>
/// Agent 查找抽象接口，由主项目适配 AgentStore 实现。
/// 避免 MicroClaw.Skills 直接引用 MicroClaw.Agent 造成循环依赖。
/// </summary>
public interface IAgentLookup
{
    string? GetIdByName(string name);
    string? GetDefaultId();
}
