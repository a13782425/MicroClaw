using Microsoft.Extensions.AI;

namespace MicroClaw.Tools;

/// <summary>
/// 内置工具提供者接口 — 实现此接口并注册到 DI（AddSingleton&lt;IBuiltinToolProvider, MyProvider&gt;），
/// AgentRunner 将自动加载该提供者的工具，无需修改核心代码。
/// </summary>
public interface IBuiltinToolProvider
{
    /// <summary>工具分组标识，对应 ToolGroupConfig.GroupId，用于按 Agent 配置启用/禁用。</summary>
    string GroupId { get; }

    /// <summary>返回工具的元数据描述列表（不需要会话信息，用于 UI 展示）。</summary>
    IReadOnlyList<(string Name, string Description)> GetToolDescriptions();

    /// <summary>
    /// 创建工具实例列表。
    /// 不依赖会话的工具忽略 sessionId；需要会话上下文的工具在 sessionId 为空时返回空列表。
    /// </summary>
    IReadOnlyList<AIFunction> CreateTools(string? sessionId);
}
