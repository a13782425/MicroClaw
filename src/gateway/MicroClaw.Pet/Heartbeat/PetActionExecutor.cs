using MicroClaw.Abstractions;
using MicroClaw.Pet.Prompt;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.Storage;
using MicroClaw.Pet.Decision;
using MicroClaw.Providers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet.Heartbeat;

/// <summary>
/// 向用户推送 Pet 通知的抽象接口。
/// 由主项目通过 SignalR <c>IHubContext&lt;GatewayHub&gt;</c> 实现。
/// </summary>
public interface IPetNotifier
{
    Task NotifyUserAsync(string sessionId, string message, CancellationToken ct = default);

    /// <summary>Pet 状态变更事件。</summary>
    Task NotifyStateChangedAsync(string sessionId, string newState, string? reason = null, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Pet 动作开始事件。</summary>
    Task NotifyActionStartedAsync(string sessionId, string actionType, string? parameter = null, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Pet 动作完成事件。</summary>
    Task NotifyActionCompletedAsync(string sessionId, string actionType, bool succeeded, string? error = null, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>
/// Pet 计划动作执行器。
/// <para>
/// 负责分发并执行 <see cref="PetStateMachineDecision.PlannedActions"/> 中的各类自主行为：
/// <list type="bullet">
///   <item><c>FetchWeb</c> — 抓取网页内容并写入 Pet RAG</item>
///   <item><c>SummarizeToMemory</c> — 将内容摘要写入 Pet 私有 RAG</item>
///   <item><c>OrganizeMemory</c> — 整理 Pet RAG 记忆（合并/去重/归类）</item>
///   <item><c>ReflectOnSession</c> — 反思会话，生成洞察写入 RAG</item>
///   <item><c>EvolvePrompts</c> — 触发 <see cref="PetPromptEvolver"/> 进化提示词</item>
///   <item><c>NotifyUser</c> — 通过 <see cref="IPetNotifier"/> 向用户推送通知</item>
///   <item><c>DelegateToAgent</c> — 委派任务给指定 Agent 执行</item>
/// </list>
/// 每个动作执行失败不中断后续动作，失败仅记录日志和 journal。
/// </para>
/// </summary>
public sealed class PetActionExecutor
{
    private readonly PetPromptEvolver _promptEvolver;
    private readonly PetStateStore _stateStore;
    private readonly PetRateLimiter _rateLimiter;
    private readonly PetModelSelector _modelSelector;
    private readonly ProviderService _providerService;
    private readonly IPetNotifier _petNotifier;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PetActionExecutor> _logger;

    public PetActionExecutor(IServiceProvider sp)
    {
        _promptEvolver = sp.GetRequiredService<PetPromptEvolver>();
        _stateStore = sp.GetRequiredService<PetStateStore>();
        _rateLimiter = sp.GetRequiredService<PetRateLimiter>();
        _modelSelector = sp.GetRequiredService<PetModelSelector>();
        _providerService = sp.GetRequiredService<ProviderService>();
        _petNotifier = sp.GetRequiredService<IPetNotifier>();
        _httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        _logger = sp.GetRequiredService<ILogger<PetActionExecutor>>();
    }

    /// <summary>仅供测试使用：可注入 null 的 notifier/httpClientFactory。</summary>
    internal PetActionExecutor(
        PetPromptEvolver promptEvolver,
        PetStateStore stateStore,
        PetRateLimiter rateLimiter,
        PetModelSelector modelSelector,
        ProviderService providerService,
        IPetNotifier? petNotifier,
        IHttpClientFactory? httpClientFactory,
        ILogger<PetActionExecutor> logger,
        bool _ = false)  // disambiguator
    {
        _promptEvolver = promptEvolver;
        _stateStore = stateStore;
        _rateLimiter = rateLimiter;
        _modelSelector = modelSelector;
        _providerService = providerService;
        _petNotifier = petNotifier!;
        _httpClientFactory = httpClientFactory!;
        _logger = logger;
    }

    /// <summary>
    /// 执行一组计划动作。每个动作独立执行，单个失败不中断后续动作。
    /// </summary>
    /// <param name="sessionId">Session ID。</param>
    /// <param name="actions">要执行的计划动作列表。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>各动作的执行结果。</returns>
    public async Task<IReadOnlyList<ActionExecutionResult>> ExecuteAsync(
        string sessionId,
        IReadOnlyList<PetPlannedAction> actions,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (actions is not { Count: > 0 })
            return [];

        var results = new List<ActionExecutionResult>(actions.Count);

        foreach (var action in actions)
        {
            if (ct.IsCancellationRequested) break;

            var result = await ExecuteSingleAsync(sessionId, action, ct);
            results.Add(result);

            // 记录 journal
            string detail = result.Succeeded
                ? $"action={action.Type}, parameter={action.Parameter ?? "(none)"}"
                : $"action={action.Type}, parameter={action.Parameter ?? "(none)"}, error={result.Error}";
            await SafeAppendJournalAsync(sessionId, result.Succeeded ? "action_completed" : "action_failed", detail, ct);
        }

        return results;
    }

    private async Task<ActionExecutionResult> ExecuteSingleAsync(
        string sessionId, PetPlannedAction action, CancellationToken ct)
    {
        try
        {
            _logger.LogDebug("Pet [{SessionId}] 执行动作: {ActionType}, Parameter={Parameter}",
                sessionId, action.Type, action.Parameter ?? "(none)");

            await _petNotifier.NotifyActionStartedAsync(sessionId, action.Type.ToString(), action.Parameter, ct);

            var result = action.Type switch
            {
                PetActionType.FetchWeb => await ExecuteFetchWebAsync(sessionId, action, ct),
                PetActionType.SummarizeToMemory => await ExecuteSummarizeToMemoryAsync(sessionId, action, ct),
                PetActionType.OrganizeMemory => await ExecuteOrganizeMemoryAsync(sessionId, action, ct),
                PetActionType.ReflectOnSession => await ExecuteReflectOnSessionAsync(sessionId, action, ct),
                PetActionType.EvolvePrompts => await ExecuteEvolvePromptsAsync(sessionId, action, ct),
                PetActionType.NotifyUser => await ExecuteNotifyUserAsync(sessionId, action, ct),
                PetActionType.DelegateToAgent => await ExecuteDelegateToAgentAsync(sessionId, action, ct),
                _ => new ActionExecutionResult(action.Type, false, $"未知动作类型: {action.Type}"),
            };

            await _petNotifier.NotifyActionCompletedAsync(sessionId, action.Type.ToString(), result.Succeeded, result.Error, ct);

            return result;
        }
        catch (OperationCanceledException)
        {
            return new ActionExecutionResult(action.Type, false, "操作已取消");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pet [{SessionId}] 动作 {ActionType} 执行失败", sessionId, action.Type);
            return new ActionExecutionResult(action.Type, false, ex.Message);
        }
    }

    // ── FetchWeb：抓取网页并写入 Pet RAG ────────────────────────────────

    private async Task<ActionExecutionResult> ExecuteFetchWebAsync(
        string sessionId, PetPlannedAction action, CancellationToken ct)
    {
        string? url = action.Parameter;
        if (string.IsNullOrWhiteSpace(url))
            return new ActionExecutionResult(PetActionType.FetchWeb, false, "未提供 URL 参数");

        // 安全校验：仅允许 http/https
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return new ActionExecutionResult(PetActionType.FetchWeb, false, $"不支持的 URL scheme: {url}");

        var client = _httpClientFactory.CreateClient("fetch");
        using var response = await client.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            return new ActionExecutionResult(PetActionType.FetchWeb, false,
                $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        string content = await response.Content.ReadAsStringAsync(ct);

        // 截断过长内容
        const int maxLength = 50000;
        if (content.Length > maxLength)
            content = content[..maxLength];

        // TODO: Reimplement with MicroRag — write fetched content to Pet RAG
        // await petRag.IngestAsync(content, sourceId: $"fetch:{url}");

        _logger.LogInformation("Pet [{SessionId}] FetchWeb 完成: {Url}, {Length} 字符 (写入 RAG 暂时禁用)",
            sessionId, url, content.Length);
        return new ActionExecutionResult(PetActionType.FetchWeb, true);
    }

    // ── SummarizeToMemory：摘要内容写入 Pet RAG ─────────────────────────

    private async Task<ActionExecutionResult> ExecuteSummarizeToMemoryAsync(
        string sessionId, PetPlannedAction action, CancellationToken ct)
    {
        string? content = action.Parameter;
        if (string.IsNullOrWhiteSpace(content))
            return new ActionExecutionResult(PetActionType.SummarizeToMemory, false, "未提供摘要内容");

        // TODO: Reimplement with MicroRag — write summary to Pet RAG
        // await petRag.IngestAsync(content);

        _logger.LogInformation("Pet [{SessionId}] SummarizeToMemory 完成, {Length} 字符 (写入 RAG 暂时禁用)",
            sessionId, content.Length);
        return new ActionExecutionResult(PetActionType.SummarizeToMemory, true);
    }

    // ── OrganizeMemory：整理 Pet RAG（调用 LLM 总结 + 剪枝）──────────────

    private async Task<ActionExecutionResult> ExecuteOrganizeMemoryAsync(
        string sessionId, PetPlannedAction action, CancellationToken ct)
    {
        // 检查速率限制
        bool acquired = await _rateLimiter.TryAcquireAsync(sessionId, ct);
        if (!acquired)
            return new ActionExecutionResult(PetActionType.OrganizeMemory, false, "速率超限，跳过整理");

        // TODO: Reimplement with MicroRag
        int chunkCount = 0;
        if (chunkCount == 0)
            return new ActionExecutionResult(PetActionType.OrganizeMemory, true, "知识库为空，无需整理");

        // TODO: Reimplement — Get knowledge sample from MicroRag
        string knowledgeSample = "";

        var provider = _modelSelector.Select(PetModelScenario.Reflecting);
        if (provider is null)
            return new ActionExecutionResult(PetActionType.OrganizeMemory, false, "无可用 Provider");

        var chatProvider = _providerService.TryGetProvider(provider.Id)
            ?? throw new InvalidOperationException($"Chat provider '{provider.Id}' is not available.");
        var organizeCtx = MicroChatContext.ForSystem(sessionId, "pet-action:organize-memory", ct);
        var response = await chatProvider.ChatAsync(
            organizeCtx,
            [
                new ChatMessage(ChatRole.System,
                    "你是记忆整理助手。请将以下知识片段进行归纳总结，去除重复内容，生成精炼的知识摘要。" +
                    "输出纯文本，每个知识点一段。"),
                new ChatMessage(ChatRole.User, knowledgeSample),
            ]);

        string summary = (response.Text ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(summary))
        {
            // TODO: Reimplement with MicroRag
            // await petRag.IngestAsync(summary, sourceId: $"organize:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        }

        // TODO: Reimplement pruning with MicroRag
        // await petRag.PruneIfNeededAsync();

        _logger.LogInformation("Pet [{SessionId}] OrganizeMemory 完成, 原有 {Count} 块",
            sessionId, chunkCount);
        return new ActionExecutionResult(PetActionType.OrganizeMemory, true);
    }

    // ── ReflectOnSession：反思会话，生成洞察 ────────────────────────────

    private async Task<ActionExecutionResult> ExecuteReflectOnSessionAsync(
        string sessionId, PetPlannedAction action, CancellationToken ct)
    {
        // 检查速率限制
        bool acquired = await _rateLimiter.TryAcquireAsync(sessionId, ct);
        if (!acquired)
            return new ActionExecutionResult(PetActionType.ReflectOnSession, false, "速率超限，跳过反思");

        // 收集反思上下文：最近 journal
        string petDir = Path.Combine(GetSessionsDir(), sessionId, "pet");
        string journalFile = Path.Combine(petDir, "journal.jsonl");
        string journalContext = "（无日志）";
        if (File.Exists(journalFile))
        {
            var lines = await File.ReadAllLinesAsync(journalFile, ct);
            journalContext = string.Join("\n", lines.TakeLast(30));
        }

        var provider = _modelSelector.Select(PetModelScenario.Reflecting);
        if (provider is null)
            return new ActionExecutionResult(PetActionType.ReflectOnSession, false, "无可用 Provider");

        var chatProvider = _providerService.TryGetProvider(provider.Id)
            ?? throw new InvalidOperationException($"Chat provider '{provider.Id}' is not available.");
        var reflectCtx = MicroChatContext.ForSystem(sessionId, "pet-action:reflect-on-session", ct);
        var response = await chatProvider.ChatAsync(
            reflectCtx,
            [
                new ChatMessage(ChatRole.System,
                    "你是会话反思助手。请根据以下会话日志，总结关键模式、用户偏好和改进建议。" +
                    "输出简洁的反思洞察（中文），每个洞察一段。"),
                new ChatMessage(ChatRole.User, $"会话 {sessionId} 最近日志：\n{journalContext}"),
            ]);

        string insight = (response.Text ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(insight))
        {
            // TODO: Reimplement with MicroRag
            // await petRag.IngestAsync(insight, sourceId: $"reflect:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        }

        _logger.LogInformation("Pet [{SessionId}] ReflectOnSession 完成", sessionId);
        return new ActionExecutionResult(PetActionType.ReflectOnSession, true);
    }

    // ── EvolvePrompts：触发提示词进化 ───────────────────────────────────

    private async Task<ActionExecutionResult> ExecuteEvolvePromptsAsync(
        string sessionId, PetPlannedAction action, CancellationToken ct)
    {
        bool success = await _promptEvolver.EvolveAsync(sessionId, action.Reason, ct);
        return new ActionExecutionResult(PetActionType.EvolvePrompts, success,
            success ? null : "提示词进化失败（可能速率超限或 LLM 解析失败）");
    }

    // ── NotifyUser：通过 SignalR 推送通知 ────────────────────────────────

    private async Task<ActionExecutionResult> ExecuteNotifyUserAsync(
        string sessionId, PetPlannedAction action, CancellationToken ct)
    {
        string? message = action.Parameter;
        if (string.IsNullOrWhiteSpace(message))
            return new ActionExecutionResult(PetActionType.NotifyUser, false, "未提供通知内容");

        await _petNotifier.NotifyUserAsync(sessionId, message, ct);

        _logger.LogInformation("Pet [{SessionId}] NotifyUser: {Message}",
            sessionId, message.Length > 100 ? message[..100] + "..." : message);
        return new ActionExecutionResult(PetActionType.NotifyUser, true);
    }

    // ── DelegateToAgent：委派 Agent（记录意图，实际执行需由 MicroPet 协调）──

    private Task<ActionExecutionResult> ExecuteDelegateToAgentAsync(
        string sessionId, PetPlannedAction action, CancellationToken ct)
    {
        // 心跳阶段的 DelegateToAgent 仅记录意图到 journal，
        // 真正的 Agent 委派需要用户消息上下文，在消息流中处理。
        string? agentId = action.Parameter;
        if (string.IsNullOrWhiteSpace(agentId))
            return Task.FromResult(new ActionExecutionResult(PetActionType.DelegateToAgent, false, "未提供 AgentId"));

        _logger.LogInformation("Pet [{SessionId}] DelegateToAgent 意图记录: AgentId={AgentId}",
            sessionId, agentId);
        return Task.FromResult(new ActionExecutionResult(PetActionType.DelegateToAgent, true));
    }

    // ── 辅助 ─────────────────────────────────────────────────────────────

    private string GetSessionsDir()
    {
        return MicroClaw.Configuration.MicroClawConfig.Env.SessionsDir;
    }

    private async Task SafeAppendJournalAsync(string sessionId, string eventType, string detail, CancellationToken ct)
    {
        try
        {
            await _stateStore.AppendJournalAsync(sessionId, eventType, detail, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pet [{SessionId}] journal 写入失败", sessionId);
        }
    }
}

/// <summary>单个动作的执行结果。</summary>
public sealed record ActionExecutionResult(
    PetActionType ActionType,
    bool Succeeded,
    string? Error = null);
