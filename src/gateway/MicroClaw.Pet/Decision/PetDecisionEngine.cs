using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;
using MicroClaw.Tools;

namespace MicroClaw.Pet.Decision;

/// <summary>
/// Pet 消息调度决策引擎：每条用户消息到达时，调用 LLM 决定委派哪个 Agent、
/// 使用哪个 Provider、启用/禁用哪些工具、是否注入 Pet 私有知识。
/// <para>
/// 输入：用户消息 + 会话历史 + 可用 Agent 列表 + 可用 Provider 列表 + Pet 状态/情绪 + Pet RAG 知识。
/// 输出：<see cref="PetDispatchResult"/>。
/// </para>
/// <para>
/// 受速率限制约束：超限时跳过 LLM 调用，直接返回默认委派决策。
/// </para>
/// </summary>
public sealed class PetDecisionEngine(
    PetRateLimiter rateLimiter,
    PetModelSelector modelSelector,
    PetStateStore stateStore,
    IEmotionStore emotionStore,
    ProviderClientFactory clientFactory,
    ILogger<PetDecisionEngine> logger)
{
    private readonly PetRateLimiter _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
    private readonly PetModelSelector _modelSelector = modelSelector ?? throw new ArgumentNullException(nameof(modelSelector));
    private readonly PetStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly IEmotionStore _emotionStore = emotionStore ?? throw new ArgumentNullException(nameof(emotionStore));
    private readonly ProviderClientFactory _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    private readonly ILogger<PetDecisionEngine> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// 对用户消息执行调度决策。
    /// </summary>
    /// <param name="context">决策上下文（用户消息、可用资源、Pet 状态等）。</param>
    /// <param name="preferredProviderId">PetConfig 中的首选 Provider ID。</param>
    /// <param name="sessionId">Session ID，用于速率限制和日志记录。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>调度决策结果。</returns>
    public async Task<PetDispatchResult> DecideAsync(
        PetDecisionContext context,
        string? preferredProviderId,
        string sessionId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        // ── 速率超限：跳过 LLM，返回默认委派 ──
        bool acquired = await _rateLimiter.TryAcquireAsync(sessionId, ct);
        if (!acquired)
        {
            _logger.LogWarning("Pet [{SessionId}] 调度决策：速率超限，回退默认委派", sessionId);
            return DefaultDispatch("速率配额不足，使用默认 Agent 和 Provider。");
        }

        // ── 选择 Provider ──
        var provider = _modelSelector.Select(PetModelScenario.Dispatch, preferredProviderId);
        if (provider is null)
        {
            _logger.LogError("Pet [{SessionId}] 调度决策：无可用 Provider", sessionId);
            return DefaultDispatch("无可用 Provider，使用默认 Agent。");
        }

        // ── 构建 Prompt 并调用 LLM ──
        string systemPrompt = PetDecisionEnginePrompt.BuildSystemPrompt();
        string userPrompt = PetDecisionEnginePrompt.BuildUserPrompt(context);

        try
        {
            var client = _clientFactory.Create(provider);
            var response = await client.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, userPrompt),
                ],
                cancellationToken: ct);

            string responseText = (response.Text ?? string.Empty).Trim();
            _logger.LogDebug("Pet [{SessionId}] 调度决策 LLM 响应: {Response}", sessionId, responseText);

            var result = ParseDispatchResult(responseText);

            // 记录 journal
            await _stateStore.AppendJournalAsync(
                sessionId,
                "dispatch_decision",
                $"agent={result.AgentId ?? "default"}, provider={result.ProviderId ?? "default"}, petRespond={result.ShouldPetRespond}, reason={result.Reason}",
                ct);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Pet [{SessionId}] 调度决策 LLM 调用失败", sessionId);
            return DefaultDispatch($"LLM 调用失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 解析 LLM 输出的 JSON 为 <see cref="PetDispatchResult"/>。
    /// 容错处理：支持 Markdown 代码块包裹、字段缺失等情况。
    /// </summary>
    internal static PetDispatchResult ParseDispatchResult(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return DefaultDispatch("[回退] LLM 返回空响应");

        string json = ExtractJson(responseText);

        try
        {
            var dto = JsonSerializer.Deserialize<DispatchResultDto>(json, JsonOptions);
            if (dto is null)
                return DefaultDispatch("[回退] JSON 反序列化为 null");

            // 构建 ToolOverrides
            List<ToolGroupConfig> toolOverrides = [];
            if (dto.ToolOverrides is { Count: > 0 })
            {
                foreach (var t in dto.ToolOverrides)
                {
                    if (!string.IsNullOrWhiteSpace(t.GroupId))
                    {
                        toolOverrides.Add(new ToolGroupConfig(
                            t.GroupId,
                            t.IsEnabled,
                            t.DisabledToolNames ?? []));
                    }
                }
            }

            return new PetDispatchResult
            {
                AgentId = NullIfEmpty(dto.AgentId),
                ProviderId = NullIfEmpty(dto.ProviderId),
                ToolOverrides = toolOverrides,
                PetKnowledge = NullIfEmpty(dto.PetKnowledge),
                ShouldPetRespond = dto.ShouldPetRespond,
                PetResponse = NullIfEmpty(dto.PetResponse),
                Reason = dto.Reason ?? string.Empty,
            };
        }
        catch (JsonException)
        {
            return DefaultDispatch($"[回退] JSON 解析失败: {json[..Math.Min(json.Length, 200)]}");
        }
    }

    /// <summary>
    /// 从可能包含 Markdown 代码块的文本中提取 JSON。
    /// </summary>
    internal static string ExtractJson(string text)
    {
        if (text.Contains("```"))
        {
            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start >= 0 && end > start)
                return text[start..(end + 1)];
        }

        return text;
    }

    private static PetDispatchResult DefaultDispatch(string reason) => new()
    {
        AgentId = null,
        ProviderId = null,
        ToolOverrides = [],
        PetKnowledge = null,
        ShouldPetRespond = false,
        PetResponse = null,
        Reason = reason,
    };

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) || s == "null" ? null : s;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── JSON DTO（宽松匹配 LLM 输出）──

    private sealed class DispatchResultDto
    {
        [JsonPropertyName("agentId")]
        public string? AgentId { get; set; }

        [JsonPropertyName("providerId")]
        public string? ProviderId { get; set; }

        [JsonPropertyName("toolOverrides")]
        public List<ToolOverrideDto>? ToolOverrides { get; set; }

        [JsonPropertyName("petKnowledge")]
        public string? PetKnowledge { get; set; }

        [JsonPropertyName("shouldPetRespond")]
        public bool ShouldPetRespond { get; set; }

        [JsonPropertyName("petResponse")]
        public string? PetResponse { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

    private sealed class ToolOverrideDto
    {
        [JsonPropertyName("groupId")]
        public string? GroupId { get; set; }

        [JsonPropertyName("isEnabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonPropertyName("disabledToolNames")]
        public List<string>? DisabledToolNames { get; set; }
    }
}
