using MicroClaw.Tools;

namespace MicroClaw.Agent;

/// <summary>
/// Pet 编排层传递给 AgentRunner 的覆盖参数。
/// 当 Pet 启用时，由 MicroPet 根据 PetDecisionEngine 的调度决策和情绪映射构建，
/// 传入 <see cref="AgentRunner.StreamReActAsync"/> 以覆盖 Agent 的默认行为。
/// </summary>
public sealed record PetOverrides
{
    /// <summary>行为模式 Temperature 覆盖（由情绪→行为映射器生成）。null 不覆盖。</summary>
    public float? Temperature { get; init; }

    /// <summary>行为模式 TopP 覆盖（由情绪→行为映射器生成）。null 不覆盖。</summary>
    public float? TopP { get; init; }

    /// <summary>行为模式的系统提示后缀（由情绪→行为映射器生成）。null 不追加。</summary>
    public string? BehaviorSuffix { get; init; }

    /// <summary>
    /// 工具分组覆盖配置。非空时替代 Agent 的默认 <see cref="AgentConfig.ToolGroupConfigs"/>。
    /// null 表示使用 Agent 默认配置。
    /// </summary>
    public IReadOnlyList<ToolGroupConfig>? ToolOverrides { get; init; }

    /// <summary>
    /// 从 Pet 私有 RAG 检索到的上下文知识，注入到 System Prompt 中。
    /// null 表示无额外知识。
    /// </summary>
    public string? PetKnowledge { get; init; }
}
