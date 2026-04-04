using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Tools;

namespace MicroClaw.Pet.Decision;

/// <summary>
/// PetDecisionEngine 的 Prompt 构建器。
/// 描述可用 Agent 的能力特长、Provider 的特性、工具组列表、Pet 自身知识和决策原则，
/// 指导 LLM 做出消息调度决策。
/// </summary>
internal static class PetDecisionEnginePrompt
{
    /// <summary>
    /// 构建系统提示词。
    /// </summary>
    internal static string BuildSystemPrompt() =>
        """
        你是 Pet 消息调度决策引擎。每当用户发来一条消息，你需要决定如何处理这条消息。

        ## 你的职责

        分析用户消息的意图，结合可用的 Agent、Provider 和工具，做出最优调度决策。你的输出会被解析为结构化 JSON 指令。

        ## 决策维度

        你需要决定以下内容：

        1. **由谁处理**：
           - 委派给某个 Agent 执行（大多数情况）
           - 由 Pet 自己直接回复（仅限询问 Pet 状态、简单闲聊等）

        2. **使用哪个模型**（Provider）：
           - null = 使用默认路由策略（推荐）
           - 指定 ProviderId = 强制使用特定模型（仅在有明确理由时）

        3. **工具配置**（可选覆盖）：
           - 空列表 = 使用 Agent 默认工具配置（推荐）
           - 指定 toolOverrides = 覆盖 Agent 的工具启用/禁用

        4. **知识注入**（可选）：
           - 如果 Pet 有与本次消息相关的私有知识，可以注入给 Agent

        ## 决策原则

        1. **默认委派**：绝大多数消息应委派给 Agent 处理，而非 Pet 自己回复。
        2. **Agent 选择**：优先使用默认 Agent；仅当消息明确需要特定 Agent 的专长时才切换。
        3. **Provider 选择**：通常保持 null（默认路由）。复杂推理任务可考虑高质量 Provider。
        4. **工具覆盖**：通常保持空列表。仅在用户明确提到需要/不需要某类工具时才覆盖。
        5. **Pet 自回复**：仅在用户问 Pet 本身状态、简单问候/闲聊、或消息不需要 Agent 能力时才由 Pet 回复。
        6. **知识注入**：如果 Pet 拥有直接相关的背景知识，应注入以增强 Agent 的回答。
        7. **速率意识**：当配额紧张时，更倾向使用默认配置，减少不必要的决策。

        ## 输出格式

        你必须且只能输出一个合法的 JSON 对象，不要包含任何其他文本、解释或 Markdown 代码块标记。格式如下：

        {
          "agentId": "目标 Agent ID 或 null（使用默认）",
          "providerId": "目标 Provider ID 或 null（使用默认路由）",
          "toolOverrides": [],
          "petKnowledge": "注入给 Agent 的 Pet 知识，或 null",
          "shouldPetRespond": false,
          "petResponse": "Pet 自回复内容（仅 shouldPetRespond=true 时填写）",
          "reason": "简短决策原因"
        }

        当 shouldPetRespond=true 时，agentId/providerId/toolOverrides 将被忽略，请在 petResponse 中填写完整的回复。

        toolOverrides 数组中每个元素为：
        {
          "groupId": "工具分组标识",
          "isEnabled": true,
          "disabledToolNames": ["可选的单独禁用工具名"]
        }
        """;

    /// <summary>
    /// 构建 User Prompt：包含用户消息、会话历史摘要、可用资源和 Pet 状态。
    /// </summary>
    internal static string BuildUserPrompt(PetDecisionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var sb = new System.Text.StringBuilder(4096);

        sb.AppendLine("# 消息调度决策请求");
        sb.AppendLine();

        // 用户消息
        sb.AppendLine("## 用户消息");
        sb.AppendLine(context.UserMessage);
        sb.AppendLine();

        // 会话历史摘要
        if (context.RecentMessageSummaries.Count > 0)
        {
            sb.AppendLine("## 最近会话上下文");
            foreach (var msg in context.RecentMessageSummaries)
                sb.AppendLine($"- {msg}");
            sb.AppendLine();
        }

        // 可用 Agent
        sb.AppendLine("## 可用 Agent");
        if (context.AvailableAgents.Count == 0)
        {
            sb.AppendLine("（无可用 Agent）");
        }
        else
        {
            foreach (var a in context.AvailableAgents)
            {
                sb.Append($"- **{a.Id}** ({a.Name})");
                if (!string.IsNullOrWhiteSpace(a.Description))
                    sb.Append($": {a.Description}");
                if (a.IsDefault)
                    sb.Append(" [默认]");
                sb.AppendLine();
            }
        }
        sb.AppendLine();

        // 可用 Provider
        sb.AppendLine("## 可用 Provider");
        if (context.AvailableProviders.Count == 0)
        {
            sb.AppendLine("（无可用 Provider）");
        }
        else
        {
            foreach (var p in context.AvailableProviders)
            {
                sb.Append($"- **{p.Id}** ({p.DisplayName}): 模型={p.ModelName}, 质量={p.QualityScore}");
                if (p.InputPricePerMToken.HasValue)
                    sb.Append($", 输入价格=${p.InputPricePerMToken}/MToken");
                if (p.IsDefault)
                    sb.Append(" [默认]");
                sb.AppendLine();
            }
        }
        sb.AppendLine();

        // 工具组
        if (context.AvailableToolGroups.Count > 0)
        {
            sb.AppendLine("## 可用工具组");
            foreach (var group in context.AvailableToolGroups)
                sb.AppendLine($"- {group}");
            sb.AppendLine();
        }

        // Pet 状态
        sb.AppendLine("## Pet 当前状态");
        sb.AppendLine($"- 行为状态: {context.BehaviorState}");
        sb.AppendLine($"- 情绪: 警觉度={context.EmotionState.Alertness}, 心情={context.EmotionState.Mood}, 好奇心={context.EmotionState.Curiosity}, 信心={context.EmotionState.Confidence}");
        sb.AppendLine();

        // 速率限制
        if (context.RateLimitStatus is { } rl)
        {
            sb.AppendLine("## 速率配额");
            sb.AppendLine($"- 剩余: {rl.RemainingCalls}/{rl.MaxCalls}");
            if (rl.IsExhausted)
                sb.AppendLine("- ⚠️ 配额已耗尽");
            sb.AppendLine();
        }

        // Pet 私有知识
        if (!string.IsNullOrWhiteSpace(context.PetRagKnowledge))
        {
            sb.AppendLine("## Pet 私有知识（与本消息相关）");
            sb.AppendLine(context.PetRagKnowledge);
            sb.AppendLine();
        }

        sb.AppendLine("请根据以上信息输出你的调度决策 JSON。");

        return sb.ToString();
    }
}

/// <summary>
/// PetDecisionEngine 的决策输入上下文。
/// </summary>
public sealed record PetDecisionContext
{
    /// <summary>用户原始消息。</summary>
    public required string UserMessage { get; init; }

    /// <summary>最近会话消息摘要。</summary>
    public IReadOnlyList<string> RecentMessageSummaries { get; init; } = [];

    /// <summary>可用 Agent 摘要列表。</summary>
    public IReadOnlyList<AgentSummary> AvailableAgents { get; init; } = [];

    /// <summary>可用 Provider 摘要列表。</summary>
    public IReadOnlyList<ProviderSummary> AvailableProviders { get; init; } = [];

    /// <summary>可用工具组标识列表（GroupId 描述，如 "cron - 定时任务"、"MCP-name"）。</summary>
    public IReadOnlyList<string> AvailableToolGroups { get; init; } = [];

    /// <summary>Pet 当前行为状态。</summary>
    public PetBehaviorState BehaviorState { get; init; }

    /// <summary>Pet 当前情绪状态。</summary>
    public EmotionState EmotionState { get; init; } = EmotionState.Default;

    /// <summary>速率限制状态。</summary>
    public RateLimitStatus? RateLimitStatus { get; init; }

    /// <summary>Pet 私有 RAG 检索到的相关知识（可选）。</summary>
    public string? PetRagKnowledge { get; init; }
}
