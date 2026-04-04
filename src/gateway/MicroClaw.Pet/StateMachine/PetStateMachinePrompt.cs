using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine.States;

namespace MicroClaw.Pet.StateMachine;

/// <summary>
/// PetStateMachine 的 Prompt 构建器。
/// 根据 <see cref="PetStateRegistry"/> 动态生成状态表，支持外部注册自定义状态。
/// </summary>
public sealed class PetStateMachinePrompt(PetStateRegistry stateRegistry)
{
    private readonly PetStateRegistry _stateRegistry = stateRegistry ?? throw new ArgumentNullException(nameof(stateRegistry));

    /// <summary>
    /// 构建系统提示词，状态表由 <see cref="PetStateRegistry"/> 动态生成。
    /// </summary>
    public string BuildSystemPrompt()
    {
        var sb = new System.Text.StringBuilder(4096);

        sb.AppendLine("""
            你是一个名为 Pet 的会话编排 AI 的内部状态决策引擎。你的任务是根据当前自我感知报告，决定 Pet 应该切换到什么行为状态、情绪如何变化、以及是否需要执行自主动作。

            ## 你的身份

            你不是直接面向用户的 AI。你是 Pet 的"大脑"——负责分析 Pet 当前处境并做出最优决策。你的输出会被解析为结构化 JSON 指令。

            ## 可用的行为状态

            | 状态 | 含义 | 适用场景 |
            |------|------|---------|
            """);

        foreach (var state in _stateRegistry.All.OrderBy(s => s.Type))
        {
            sb.AppendLine($"| {state.DisplayName} | {state.Description} | {state.ApplicableScenes} |");
        }

        // 附加各状态的特殊说明片段（若有）
        var fragments = _stateRegistry.All
            .Where(s => s.StateMachinePromptFragment is not null)
            .ToList();
        if (fragments.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("### 状态特殊说明");
            foreach (var s in fragments)
            {
                sb.AppendLine($"- **{s.DisplayName}**：{s.StateMachinePromptFragment}");
            }
        }

        sb.AppendLine("""

            ## 可用的自主动作类型

            | 类型 | 说明 | Parameter |
            |------|------|-----------|
            | FetchWeb | 从网页获取内容学习 | URL 地址 |
            | SummarizeToMemory | 将内容摘要存入 Pet 私有 RAG | 要摘要的内容描述 |
            | OrganizeMemory | 整理 Pet 私有 RAG（合并/去重/归类） | 无 |
            | ReflectOnSession | 反思会话历史，生成洞察 | 无 |
            | EvolvePrompts | 进化 Pet 提示词（personality/dispatch-rules/knowledge-interests） | 无 |
            | NotifyUser | 向用户发送通知 | 消息内容 |
            | DelegateToAgent | 委派任务给指定 Agent | Agent ID |

            ## 情绪变化规则

            情绪有四个维度，各维度取值 [0, 100]，50 为平衡值：
            - **Alertness（警觉度）**：0=极度倦怠, 100=极度亢奋
            - **Mood（心情）**：0=极度低落, 100=极度愉悦
            - **Curiosity（好奇心）**：0=漠然, 100=强烈探索欲
            - **Confidence（信心）**：0=极度不确定, 100=极度自信

            你可以通过 emotionShift 对象调整四个维度（正数增加，负数减少，0 不变）。合理的单次变化幅度通常在 -15 到 +15 之间。

            ## 决策约束

            1. **速率配额**：注意 remainingCalls / maxCalls 比例。当剩余 < 20% 时应考虑节约；当剩余 = 0 时必须切换到 Resting 或 Panic。
            2. **时间感知**：考虑 Pet 上次心跳时间和当前时间，判断是否长期未活动。
            3. **动作数量**：每次心跳建议 0-3 个动作，避免一次性安排过多。
            4. **EvolvePrompts 触发时机**：不要频繁触发。仅在积累了足够的会话经验、发现明显的改进机会时才建议进化。

            ## 输出格式

            你必须且只能输出一个合法的 JSON 对象，不要包含任何其他文本、解释或 Markdown 代码块标记。格式如下：

            {
              "newState": "状态名（如 Idle, Learning, Resting 等）",
              "emotionShift": {
                "alertness": 0,
                "mood": 0,
                "curiosity": 0,
                "confidence": 0
              },
              "reason": "简短说明你的决策原因",
              "plannedActions": [
                {
                  "type": "动作类型（如 FetchWeb, SummarizeToMemory 等）",
                  "parameter": "可选参数",
                  "reason": "执行此动作的原因"
                }
              ]
            }
            """);

        return sb.ToString();
    }

    /// <summary>
    /// 将 <see cref="PetSelfAwarenessReport"/> 格式化为 LLM User Prompt。
    /// 当前状态的允许动作列表（软提示）会追加到状态块中。
    /// </summary>
    public string BuildUserPrompt(PetSelfAwarenessReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new System.Text.StringBuilder(2048);

        sb.AppendLine("# Pet 自我感知报告");
        sb.AppendLine();

        // 基本状态
        sb.AppendLine("## 当前状态");
        sb.AppendLine($"- SessionId: {report.SessionId}");
        sb.AppendLine($"- 行为状态: {report.BehaviorState}");
        sb.AppendLine($"- 行为模式: {report.BehaviorMode}");

        // 软提示：当前状态允许的动作范围
        if (_stateRegistry.TryGet(report.BehaviorState, out var stateDef) && stateDef!.AllowedActions.Count > 0)
        {
            var allowedNames = string.Join(", ", stateDef.AllowedActions.Select(a => a.ToString()));
            sb.AppendLine($"- 当前状态允许的动作（建议范围）: {allowedNames}");
        }

        sb.AppendLine();

        // 情绪
        sb.AppendLine("## 当前情绪");
        sb.AppendLine($"- 警觉度 (Alertness): {report.EmotionState.Alertness}/100");
        sb.AppendLine($"- 心情 (Mood): {report.EmotionState.Mood}/100");
        sb.AppendLine($"- 好奇心 (Curiosity): {report.EmotionState.Curiosity}/100");
        sb.AppendLine($"- 信心 (Confidence): {report.EmotionState.Confidence}/100");
        sb.AppendLine();

        // 速率限制
        if (report.RateLimitStatus is { } rl)
        {
            sb.AppendLine("## 速率配额");
            sb.AppendLine($"- 已用/上限: {rl.UsedCalls}/{rl.MaxCalls}");
            sb.AppendLine($"- 剩余: {rl.RemainingCalls}");
            sb.AppendLine($"- 是否耗尽: {(rl.IsExhausted ? "是" : "否")}");
            sb.AppendLine($"- 窗口结束: {rl.WindowEnd:yyyy-MM-dd HH:mm:ss UTC}");
            sb.AppendLine();
        }

        // Provider
        sb.AppendLine("## 可用 Provider");
        sb.AppendLine($"- 数量: {report.EnabledProviderCount}");
        if (report.PreferredProviderId is not null)
            sb.AppendLine($"- 首选: {report.PreferredProviderId}");
        foreach (var p in report.AvailableProviders)
        {
            sb.AppendLine($"  - {p.Id} ({p.DisplayName}): 模型={p.ModelName}, 质量={p.QualityScore}, 延迟={p.LatencyTier}{(p.IsDefault ? " [默认]" : "")}");
        }
        sb.AppendLine();

        // Agent
        sb.AppendLine("## 可用 Agent");
        sb.AppendLine($"- 数量: {report.EnabledAgentCount}");
        foreach (var a in report.AvailableAgents)
        {
            sb.AppendLine($"  - {a.Id} ({a.Name}): {a.Description}{(a.IsDefault ? " [默认]" : "")}");
        }
        sb.AppendLine();

        // RAG
        sb.AppendLine("## Pet 私有 RAG");
        sb.AppendLine($"- 存在: {(report.HasPetRag ? "是" : "否")}");
        sb.AppendLine($"- 分块数: {report.PetRagChunkCount}");
        sb.AppendLine();

        // 最近消息
        if (report.RecentMessageSummaries.Count > 0)
        {
            sb.AppendLine("## 最近会话消息");
            foreach (var msg in report.RecentMessageSummaries)
            {
                sb.AppendLine($"- {msg}");
            }
            sb.AppendLine();
        }

        // 时间
        sb.AppendLine("## 时间信息");
        sb.AppendLine($"- 当前时间: {report.Timestamp:yyyy-MM-dd HH:mm:ss UTC}");
        if (report.LastHeartbeatAt.HasValue)
            sb.AppendLine($"- 上次心跳: {report.LastHeartbeatAt.Value:yyyy-MM-dd HH:mm:ss UTC}");
        else
            sb.AppendLine("- 上次心跳: 从未执行");
        sb.AppendLine($"- Pet 创建时间: {report.CreatedAt:yyyy-MM-dd HH:mm:ss UTC}");

        sb.AppendLine();
        sb.AppendLine("请根据以上信息，输出你的状态决策 JSON。");

        return sb.ToString();
    }
}
