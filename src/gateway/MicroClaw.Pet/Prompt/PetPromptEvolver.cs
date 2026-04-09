using System.Text.Json;
using MicroClaw.Configuration;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet.Prompt;

/// <summary>
/// Pet 提示词进化器。
/// <para>
/// 由 <see cref="StateMachine.PetStateMachine"/> 的 <c>EvolvePrompts</c> 计划动作触发（非定时器）。
/// 分析最近 journal + 习惯数据，调用 LLM 生成提示词修改建议，写回 YAML（保留 .bak 备份），
/// 受速率限制约束。
/// </para>
/// </summary>
public sealed class PetPromptEvolver
{
    private readonly PetPromptStore _promptStore;
    private readonly PetStateStore _stateStore;
    private readonly PetRateLimiter _rateLimiter;
    private readonly PetModelSelector _modelSelector;
    private readonly ProviderClientFactory _clientFactory;
    private readonly ILogger<PetPromptEvolver> _logger;
    private readonly string _sessionsDir;

    public PetPromptEvolver(IServiceProvider sp)
    {
        _promptStore = sp.GetRequiredService<PetPromptStore>();
        _stateStore = sp.GetRequiredService<PetStateStore>();
        _rateLimiter = sp.GetRequiredService<PetRateLimiter>();
        _modelSelector = sp.GetRequiredService<PetModelSelector>();
        _clientFactory = sp.GetRequiredService<ProviderClientFactory>();
        _logger = sp.GetRequiredService<ILogger<PetPromptEvolver>>();
        _sessionsDir = MicroClawConfig.Env.SessionsDir;
    }

    /// <summary>仅供测试使用。</summary>
    internal PetPromptEvolver(
        PetPromptStore promptStore,
        PetStateStore stateStore,
        PetRateLimiter rateLimiter,
        PetModelSelector modelSelector,
        ProviderClientFactory clientFactory,
        string sessionsDir,
        ILogger<PetPromptEvolver> logger)
    {
        _promptStore = promptStore;
        _stateStore = stateStore;
        _rateLimiter = rateLimiter;
        _modelSelector = modelSelector;
        _clientFactory = clientFactory;
        _sessionsDir = sessionsDir;
        _logger = logger;
    }

    /// <summary>
    /// 执行一轮提示词进化：读取当前提示词 + 最近 journal/habits，调用 LLM 生成修改，写回 YAML。
    /// </summary>
    /// <param name="sessionId">Session ID。</param>
    /// <param name="reason">触发进化的原因（来自 PetStateMachine 决策）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>进化是否成功执行。</returns>
    public async Task<bool> EvolveAsync(string sessionId, string? reason = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        // ── 速率检查 ──
        bool acquired = await _rateLimiter.TryAcquireAsync(sessionId, ct).ConfigureAwait(false);
        if (!acquired)
        {
            _logger.LogWarning("PetPromptEvolver [{SessionId}]: 速率超限，跳过进化", sessionId);
            return false;
        }

        // ── 选择 Provider（QualityFirst：进化需要高质量模型） ──
        var state = await _stateStore.LoadAsync(sessionId, ct).ConfigureAwait(false);
        var config = await _stateStore.LoadConfigAsync(sessionId, ct).ConfigureAwait(false);
        var provider = _modelSelector.Select(PetModelScenario.PromptEvolution, config?.PreferredProviderId);
        if (provider is null)
        {
            _logger.LogError("PetPromptEvolver [{SessionId}]: 无可用 Provider", sessionId);
            return false;
        }

        // ── 收集上下文 ──
        var currentPrompts = await _promptStore.LoadAllAsTextAsync(sessionId, ct).ConfigureAwait(false);
        var recentJournal = await LoadRecentJournalAsync(sessionId, ct).ConfigureAwait(false);
        var recentHabits = await LoadRecentHabitsAsync(sessionId, ct).ConfigureAwait(false);

        // ── 构建 Prompt ──
        string systemPrompt = BuildSystemPrompt();
        string userPrompt = BuildUserPrompt(currentPrompts, recentJournal, recentHabits, reason);

        // ── 调用 LLM ──
        try
        {
            var client = _clientFactory.Create(provider);
            var response = await client.GetResponseAsync(
                [
                    new ChatMessage(ChatRole.System, systemPrompt),
                    new ChatMessage(ChatRole.User, userPrompt),
                ],
                cancellationToken: ct).ConfigureAwait(false);

            string responseText = (response.Text ?? string.Empty).Trim();
            _logger.LogDebug("PetPromptEvolver [{SessionId}] LLM 响应: {Response}", sessionId, responseText);

            var evolution = ParseEvolution(responseText);
            if (evolution is null)
            {
                _logger.LogWarning("PetPromptEvolver [{SessionId}]: LLM 返回无法解析", sessionId);
                return false;
            }

            // ── 应用进化 ──
            await ApplyEvolutionAsync(sessionId, evolution, ct).ConfigureAwait(false);

            // ── 记录 journal ──
            await _stateStore.AppendJournalAsync(
                sessionId,
                "prompt_evolution",
                $"进化完成: {evolution.Summary}",
                ct).ConfigureAwait(false);

            _logger.LogInformation("PetPromptEvolver [{SessionId}]: 提示词进化成功 — {Summary}", sessionId, evolution.Summary);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "PetPromptEvolver [{SessionId}]: LLM 调用失败", sessionId);
            return false;
        }
    }

    private async Task ApplyEvolutionAsync(string sessionId, PromptEvolution evolution, CancellationToken ct)
    {
        if (evolution.Personality is not null)
        {
            var current = await _promptStore.LoadPersonalityAsync(sessionId, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(evolution.Personality.Persona))
                current.Persona = evolution.Personality.Persona;
            if (!string.IsNullOrWhiteSpace(evolution.Personality.Tone))
                current.Tone = evolution.Personality.Tone;
            if (!string.IsNullOrWhiteSpace(evolution.Personality.Language))
                current.Language = evolution.Personality.Language;
            await _promptStore.SavePersonalityAsync(sessionId, current, ct).ConfigureAwait(false);
        }

        if (evolution.DispatchRules is not null)
        {
            var current = await _promptStore.LoadDispatchRulesAsync(sessionId, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(evolution.DispatchRules.DefaultStrategy))
                current.DefaultStrategy = evolution.DispatchRules.DefaultStrategy;
            if (evolution.DispatchRules.Rules is { Count: > 0 })
                current.Rules = evolution.DispatchRules.Rules;
            await _promptStore.SaveDispatchRulesAsync(sessionId, current, ct).ConfigureAwait(false);
        }

        if (evolution.KnowledgeInterests is not null)
        {
            var current = await _promptStore.LoadKnowledgeInterestsAsync(sessionId, ct).ConfigureAwait(false);
            if (evolution.KnowledgeInterests.Topics is { Count: > 0 })
                current.Topics = evolution.KnowledgeInterests.Topics;
            await _promptStore.SaveKnowledgeInterestsAsync(sessionId, current, ct).ConfigureAwait(false);
        }
    }

    // ── Journal / Habits 加载 ────────────────────────────────────────────

    private async Task<string> LoadRecentJournalAsync(string sessionId, CancellationToken ct)
    {
        string journalFile = Path.Combine(GetPetDir(sessionId), "journal.jsonl");
        if (!File.Exists(journalFile)) return "（无日志）";

        var lines = await File.ReadAllLinesAsync(journalFile, ct).ConfigureAwait(false);
        // 取最近 50 条
        var recent = lines.TakeLast(50);
        return string.Join("\n", recent);
    }

    private async Task<string> LoadRecentHabitsAsync(string sessionId, CancellationToken ct)
    {
        string habitsFile = Path.Combine(GetPetDir(sessionId), "habits.jsonl");
        if (!File.Exists(habitsFile)) return "（无习惯数据）";

        var lines = await File.ReadAllLinesAsync(habitsFile, ct).ConfigureAwait(false);
        var recent = lines.TakeLast(30);
        return string.Join("\n", recent);
    }

    private string GetPetDir(string sessionId) =>
        Path.Combine(_sessionsDir, sessionId, "pet");

    // ── Prompt 构建 ──────────────────────────────────────────────────────

    internal static string BuildSystemPrompt() => """
        你是一个 Pet 提示词进化引擎。你的任务是根据 Pet 最近的行为日志和会话习惯数据，
        分析当前提示词的优缺点，并提出改进建议。

        ## 进化原则
        1. **渐进式**：每次只做小幅调整，避免剧烈变化
        2. **数据驱动**：基于日志和习惯数据中的实际模式
        3. **保持核心**：保留人格设定的核心特征，只调整细节
        4. **可逆**：系统会自动保留 .bak 备份，但你仍应谨慎修改

        ## 输出格式
        请以 JSON 格式输出，只修改需要变更的部分（不需要变更的字段设为 null）：

        ```json
        {
          "summary": "本次进化的简要说明",
          "personality": {
            "persona": "新的人格描述（null = 不修改）",
            "tone": "新的语气（null = 不修改）",
            "language": "新的语言（null = 不修改）"
          },
          "dispatch_rules": {
            "default_strategy": "新的默认策略（null = 不修改）",
            "rules": [
              {"pattern": "正则", "preferred_model_type": "quality|cost|default", "notes": "说明"}
            ]
          },
          "knowledge_interests": {
            "topics": [
              {"name": "主题名", "description": "描述", "priority": "high|medium|low"}
            ]
          }
        }
        ```

        如果某个大类不需要修改，整个字段设为 null。
        如果 rules 或 topics 不需要修改，数组设为 null。
        """;

    internal static string BuildUserPrompt(string currentPrompts, string recentJournal, string recentHabits, string? reason)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("## 当前提示词");
        sb.AppendLine(currentPrompts);
        sb.AppendLine();
        sb.AppendLine("## 最近行为日志（最新 50 条）");
        sb.AppendLine(recentJournal);
        sb.AppendLine();
        sb.AppendLine("## 最近会话习惯（最新 30 条）");
        sb.AppendLine(recentHabits);

        if (!string.IsNullOrWhiteSpace(reason))
        {
            sb.AppendLine();
            sb.AppendLine("## 触发原因");
            sb.AppendLine(reason);
        }

        sb.AppendLine();
        sb.AppendLine("请分析以上数据，给出提示词进化建议。以 JSON 格式输出。");
        return sb.ToString();
    }

    // ── 解析 LLM 响应 ───────────────────────────────────────────────────

    internal static PromptEvolution? ParseEvolution(string response)
    {
        if (string.IsNullOrWhiteSpace(response)) return null;

        // 支持 Markdown 代码块包裹
        var json = ExtractJson(response);
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            return JsonSerializer.Deserialize<PromptEvolution>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractJson(string text)
    {
        // 尝试找到 ```json ... ``` 代码块
        int start = text.IndexOf("```json", StringComparison.OrdinalIgnoreCase);
        if (start >= 0)
        {
            start = text.IndexOf('\n', start);
            if (start < 0) return null;
            int end = text.IndexOf("```", start + 1, StringComparison.Ordinal);
            if (end > start)
                return text[(start + 1)..end].Trim();
        }

        // 尝试找到 ``` ... ```
        start = text.IndexOf("```", StringComparison.Ordinal);
        if (start >= 0)
        {
            start = text.IndexOf('\n', start);
            if (start < 0) return null;
            int end = text.IndexOf("```", start + 1, StringComparison.Ordinal);
            if (end > start)
                return text[(start + 1)..end].Trim();
        }

        // 尝试直接找到 { ... }
        int braceStart = text.IndexOf('{');
        int braceEnd = text.LastIndexOf('}');
        if (braceStart >= 0 && braceEnd > braceStart)
            return text[braceStart..(braceEnd + 1)];

        return null;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
}

// ── 进化结果模型 ─────────────────────────────────────────────────────────

/// <summary>LLM 返回的提示词进化结果。</summary>
internal sealed class PromptEvolution
{
    public string Summary { get; set; } = string.Empty;
    public PersonalityEvolution? Personality { get; set; }
    public DispatchRulesEvolution? DispatchRules { get; set; }
    public KnowledgeInterestsEvolution? KnowledgeInterests { get; set; }
}

internal sealed class PersonalityEvolution
{
    public string? Persona { get; set; }
    public string? Tone { get; set; }
    public string? Language { get; set; }
}

internal sealed class DispatchRulesEvolution
{
    public string? DefaultStrategy { get; set; }
    public List<DispatchRule>? Rules { get; set; }
}

internal sealed class KnowledgeInterestsEvolution
{
    public List<KnowledgeTopic>? Topics { get; set; }
}
