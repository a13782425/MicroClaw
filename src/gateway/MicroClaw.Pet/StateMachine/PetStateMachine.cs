using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using MicroClaw.Abstractions;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;

namespace MicroClaw.Pet.StateMachine;

/// <summary>
/// LLM 驱动的 Pet 状态机。
/// <para>
/// 输入 <see cref="PetSelfAwarenessReport"/>，调用轻量模型，输出 <see cref="PetStateMachineDecision"/>
/// （newState / emotionShift / reason / plannedActions[]）。
/// </para>
/// <para>
/// PlannedActions 包含 <c>evolve_prompts</c> 类型，由 LLM 自主决定何时触发提示词进化。
/// 唯一硬限制：速率超限时跳过 LLM 调用，直接返回强制 Resting/Panic 决策。
/// </para>
/// </summary>
public sealed class PetStateMachine(
    PetRateLimiter rateLimiter,
    PetModelSelector modelSelector,
    PetStateStore stateStore,
    IEmotionStore emotionStore,
    ProviderService providerService,
    PetStateMachinePrompt prompt,
    ILogger<PetStateMachine> logger)
{
    private readonly PetRateLimiter _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
    private readonly PetModelSelector _modelSelector = modelSelector ?? throw new ArgumentNullException(nameof(modelSelector));
    private readonly PetStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly IEmotionStore _emotionStore = emotionStore ?? throw new ArgumentNullException(nameof(emotionStore));
    private readonly ProviderService _providerService = providerService ?? throw new ArgumentNullException(nameof(providerService));
    private readonly PetStateMachinePrompt _prompt = prompt ?? throw new ArgumentNullException(nameof(prompt));
    private readonly ILogger<PetStateMachine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// 执行一次状态机决策：分析当前自我感知报告，调用 LLM，返回结构化决策。
    /// 速率超限时跳过 LLM 调用，直接返回强制决策。
    /// </summary>
    /// <param name="report">Pet 自我感知报告。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>状态机决策结果。</returns>
    public async Task<PetStateMachineDecision> EvaluateAsync(
        PetSelfAwarenessReport report,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        // ── 速率超限：跳过 LLM，直接返回强制决策 ──
        if (report.RateLimitStatus is { IsExhausted: true })
        {
            _logger.LogWarning("Pet [{SessionId}] 速率已耗尽，强制切换到 Resting", report.SessionId);
            var forcedDecision = new PetStateMachineDecision
            {
                NewState = PetBehaviorState.Resting,
                EmotionShift = new EmotionDelta(Alertness: -10, Mood: -5),
                Reason = "速率配额已耗尽，强制进入休息状态以等待窗口重置。",
                PlannedActions = [],
            };
            await ApplyDecisionAsync(report.SessionId, forcedDecision, ct);
            return forcedDecision;
        }

        // ── 尝试消耗一次 LLM 配额 ──
        bool acquired = await _rateLimiter.TryAcquireAsync(report.SessionId, ct);
        if (!acquired)
        {
            _logger.LogWarning("Pet [{SessionId}] 无法获取 LLM 配额，回退到 Resting", report.SessionId);
            var fallbackDecision = new PetStateMachineDecision
            {
                NewState = PetBehaviorState.Resting,
                EmotionShift = new EmotionDelta(Alertness: -5),
                Reason = "无法获取 LLM 调用配额，进入休息状态。",
                PlannedActions = [],
            };
            await ApplyDecisionAsync(report.SessionId, fallbackDecision, ct);
            return fallbackDecision;
        }

        // ── 选择 Provider ──
        var provider = _modelSelector.Select(PetModelScenario.Heartbeat, report.PreferredProviderId);
        if (provider is null)
        {
            _logger.LogError("Pet [{SessionId}] 无可用 Provider", report.SessionId);
            var noProviderDecision = new PetStateMachineDecision
            {
                NewState = PetBehaviorState.Panic,
                EmotionShift = new EmotionDelta(Alertness: 15, Mood: -10, Confidence: -15),
                Reason = "无可用 Provider，进入 Panic 状态。",
                PlannedActions = [],
            };
            await ApplyDecisionAsync(report.SessionId, noProviderDecision, ct);
            return noProviderDecision;
        }

        // ── 构建 Prompt 并调用 LLM ──
        string systemPrompt = _prompt.BuildSystemPrompt();
        string userPrompt = _prompt.BuildUserPrompt(report);

        try
        {
            var chatProvider = _providerService.TryGetProvider(provider.Id)
                ?? throw new InvalidOperationException($"Chat provider '{provider.Id}' is not available.");
            var chatCtx = MicroChatContext.ForSystem(report.SessionId, "pet-heartbeat", ct);
            var response = await chatProvider.ChatAsync(
                chatCtx,
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, userPrompt),
                ]);

            string responseText = (response.Text ?? string.Empty).Trim();
            _logger.LogDebug("Pet [{SessionId}] 状态机 LLM 响应: {Response}", report.SessionId, responseText);

            var decision = ParseDecision(responseText);
            await ApplyDecisionAsync(report.SessionId, decision, ct);
            return decision;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Pet [{SessionId}] 状态机 LLM 调用失败", report.SessionId);
            var errorDecision = new PetStateMachineDecision
            {
                NewState = report.BehaviorState, // 保持当前状态
                EmotionShift = new EmotionDelta(Confidence: -5, Mood: -3),
                Reason = $"LLM 调用失败: {ex.Message}",
                PlannedActions = [],
            };
            await ApplyDecisionAsync(report.SessionId, errorDecision, ct);
            return errorDecision;
        }
    }

    /// <summary>
    /// 将决策应用到 Pet 状态和情绪存储中。
    /// </summary>
    private async Task ApplyDecisionAsync(string sessionId, PetStateMachineDecision decision, CancellationToken ct)
    {
        // 更新行为状态
        var state = await _stateStore.LoadAsync(sessionId, ct);
        if (state is null) return;

        var updatedState = state with
        {
            BehaviorState = decision.NewState,
            LastHeartbeatAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        await _stateStore.SaveAsync(updatedState, ct);

        // 更新情绪
        if (decision.EmotionShift != EmotionDelta.Zero)
        {
            var currentEmotion = await _emotionStore.GetCurrentAsync(sessionId, ct);
            var newEmotion = currentEmotion.Apply(decision.EmotionShift);
            await _emotionStore.SaveAsync(sessionId, newEmotion, ct);
        }

        // 记录 journal
        string actionSummary = decision.PlannedActions.Count > 0
            ? string.Join(", ", decision.PlannedActions.Select(a => a.Type.ToString()))
            : "none";
        await _stateStore.AppendJournalAsync(
            sessionId,
            "state_machine_decision",
            $"newState={decision.NewState}, actions=[{actionSummary}], reason={decision.Reason}",
            ct);
    }

    /// <summary>
    /// 解析 LLM 输出的 JSON 为 <see cref="PetStateMachineDecision"/>。
    /// 容错处理：支持 Markdown 代码块包裹、字段缺失等情况。
    /// </summary>
    internal static PetStateMachineDecision ParseDecision(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return FallbackDecision("LLM 返回空响应");

        // 提取 JSON：去掉可能的 Markdown 代码块包裹
        string json = ExtractJson(responseText);

        try
        {
            var dto = JsonSerializer.Deserialize<StateMachineDecisionDto>(json, JsonOptions);
            if (dto is null)
                return FallbackDecision("JSON 反序列化为 null");

            // 解析 newState
            if (!Enum.TryParse<PetBehaviorState>(dto.NewState, ignoreCase: true, out var newState))
                newState = PetBehaviorState.Idle;

            // 解析 emotionShift
            var emotionShift = dto.EmotionShift is not null
                ? new EmotionDelta(
                    dto.EmotionShift.Alertness,
                    dto.EmotionShift.Mood,
                    dto.EmotionShift.Curiosity,
                    dto.EmotionShift.Confidence)
                : EmotionDelta.Zero;

            // 解析 plannedActions
            var actions = new List<PetPlannedAction>();
            if (dto.PlannedActions is { Count: > 0 })
            {
                foreach (var actionDto in dto.PlannedActions)
                {
                    if (Enum.TryParse<PetActionType>(actionDto.Type, ignoreCase: true, out var actionType))
                    {
                        actions.Add(new PetPlannedAction
                        {
                            Type = actionType,
                            Parameter = actionDto.Parameter,
                            Reason = actionDto.Reason,
                        });
                    }
                }
            }

            return new PetStateMachineDecision
            {
                NewState = newState,
                EmotionShift = emotionShift,
                Reason = dto.Reason ?? string.Empty,
                PlannedActions = actions,
            };
        }
        catch (JsonException)
        {
            return FallbackDecision($"JSON 解析失败: {json[..Math.Min(json.Length, 200)]}");
        }
    }

    /// <summary>
    /// 从可能包含 Markdown 代码块的文本中提取 JSON。
    /// </summary>
    internal static string ExtractJson(string text)
    {
        // 去掉 ```json ... ``` 包裹
        if (text.Contains("```"))
        {
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
                return text[start..(end + 1)];
        }

        return text;
    }

    private static PetStateMachineDecision FallbackDecision(string reason) => new()
    {
        NewState = PetBehaviorState.Idle,
        EmotionShift = EmotionDelta.Zero,
        Reason = $"[回退] {reason}",
        PlannedActions = [],
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    // ── JSON DTO（宽松匹配 LLM 输出）──

    private sealed class StateMachineDecisionDto
    {
        [JsonPropertyName("newState")]
        public string? NewState { get; set; }

        [JsonPropertyName("emotionShift")]
        public EmotionShiftDto? EmotionShift { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("plannedActions")]
        public List<PlannedActionDto>? PlannedActions { get; set; }
    }

    private sealed class EmotionShiftDto
    {
        [JsonPropertyName("alertness")]
        public int Alertness { get; set; }

        [JsonPropertyName("mood")]
        public int Mood { get; set; }

        [JsonPropertyName("curiosity")]
        public int Curiosity { get; set; }

        [JsonPropertyName("confidence")]
        public int Confidence { get; set; }
    }

    private sealed class PlannedActionDto
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("parameter")]
        public string? Parameter { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}
