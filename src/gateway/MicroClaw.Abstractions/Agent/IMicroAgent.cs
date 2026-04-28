using MicroClaw.Abstractions.Streaming;

namespace MicroClaw.Abstractions.Agent;
/// <summary>
/// 单个 Agent 的运行时契约。
/// 实现类为 MicroClaw.Agent.MicroAgent（: MicroObject），
/// 一个 Agent 配置对应一个运行时实例，由 IMicroAgentService 管理生命周期。
/// </summary>
public interface IMicroAgent
{
    string Id { get; }
    string Name { get; }
    bool IsEnabled { get; }
    bool IsDefault { get; }
    
    /// <summary>
    /// 驱动 ReAct 循环。调用方负责在 <paramref name="context"/> 中填充
    /// TargetProviderId、AssembledMessages、AssembledTools、ExecutionOptions 等执行事实；
    /// MicroAgent 仅消费 context 运行模型。
    /// AncestorAgentIds 通过 context 传入，无需额外参数。
    /// </summary>
    IAsyncEnumerable<StreamItem> StreamAsync(MicroChatContext context);
    
    /// <summary>
    /// 直接调用此 Agent 持有的某个工具并返回文本结果。
    /// 供工作流 Tool 节点使用（不走 LLM，仅执行工具函数）。
    /// </summary>
    Task<string> InvokeToolAsync(
        string toolName,
        IReadOnlyDictionary<string, string>? args,
        string fallbackInput,
        CancellationToken ct);
}