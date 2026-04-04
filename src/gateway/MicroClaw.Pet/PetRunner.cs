using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MicroClaw.Agent;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Observer;
using MicroClaw.Pet.Rag;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;
using MicroClaw.Tools;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet;

/// <summary>
/// Pet 会话编排引擎：所有用户消息先到 Pet，由 Pet（LLM）决策选择模型/Agent/工具，
/// 再委派 <see cref="AgentRunner"/> 执行。
/// <para>
/// HandleMessageAsync 流程：
/// <list type="number">
///   <item>加载 PetState + PetConfig</item>
///   <item>若 Pet 未启用，直接透传 AgentRunner</item>
///   <item>Pet RAG 检索（用户消息相关知识）</item>
///   <item>PetDecisionEngine(LLM) 调度决策</item>
///   <item>根据 dispatch 结果调用 AgentRunner.StreamReActAsync()（或 Pet 直接回复）</item>
///   <item>后处理：更新情绪、记录 journal、收集习惯</item>
/// </list>
/// 实现 <see cref="IAgentMessageHandler"/> 供渠道消息处理器路由调用。
/// </para>
/// </summary>
public sealed class PetRunner(
    AgentRunner agentRunner,
    AgentStore agentStore,
    ISessionReader sessionReader,
    ProviderConfigStore providerStore,
    PetStateStore stateStore,
    IEmotionStore emotionStore,
    IEmotionRuleEngine emotionRuleEngine,
    IEmotionBehaviorMapper emotionBehaviorMapper,
    PetDecisionEngine decisionEngine,
    PetRateLimiter rateLimiter,
    PetRagScope petRagScope,
    PetSelfAwarenessReportBuilder reportBuilder,
    PetSessionObserver sessionObserver,
    ILogger<PetRunner> logger) : IPetRunner, IAgentMessageHandler
{
    private readonly AgentRunner _agentRunner = agentRunner ?? throw new ArgumentNullException(nameof(agentRunner));
    private readonly AgentStore _agentStore = agentStore ?? throw new ArgumentNullException(nameof(agentStore));
    private readonly ISessionReader _sessionReader = sessionReader ?? throw new ArgumentNullException(nameof(sessionReader));
    private readonly ProviderConfigStore _providerStore = providerStore ?? throw new ArgumentNullException(nameof(providerStore));
    private readonly PetStateStore _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    private readonly IEmotionStore _emotionStore = emotionStore ?? throw new ArgumentNullException(nameof(emotionStore));
    private readonly IEmotionRuleEngine _emotionRuleEngine = emotionRuleEngine ?? throw new ArgumentNullException(nameof(emotionRuleEngine));
    private readonly IEmotionBehaviorMapper _emotionBehaviorMapper = emotionBehaviorMapper ?? throw new ArgumentNullException(nameof(emotionBehaviorMapper));
    private readonly PetDecisionEngine _decisionEngine = decisionEngine ?? throw new ArgumentNullException(nameof(decisionEngine));
    private readonly PetRateLimiter _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
    private readonly PetRagScope _petRagScope = petRagScope ?? throw new ArgumentNullException(nameof(petRagScope));
    private readonly PetSelfAwarenessReportBuilder _reportBuilder = reportBuilder ?? throw new ArgumentNullException(nameof(reportBuilder));
    private readonly PetSessionObserver _sessionObserver = sessionObserver ?? throw new ArgumentNullException(nameof(sessionObserver));
    private readonly ILogger<PetRunner> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // ── IPetRunner ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamItem> HandleMessageAsync(
        string sessionId,
        IReadOnlyList<SessionMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default,
        string source = "chat",
        string? channelId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        // ── 1. 加载 Pet 状态与配置 ──
        var petState = await _stateStore.LoadAsync(sessionId, ct);
        var petConfig = await _stateStore.LoadConfigAsync(sessionId, ct);

        // 未启用 Pet 时直接透传 AgentRunner
        if (petState is null || petConfig is not { Enabled: true })
        {
            _logger.LogDebug("Pet 未启用 (SessionId={SessionId})，透传 AgentRunner", sessionId);
            await foreach (var item in PassthroughToAgentRunnerAsync(sessionId, history, ct, source))
                yield return item;
            yield break;
        }

        // ── 薄迭代器：C# 不允许在含 catch 的 try 块中 yield，与 AgentRunner 相同的 Channel 解耦模式 ──
        var outputChannel = Channel.CreateUnbounded<StreamItem>();
        Task execution = ExecuteWithPetAsync(sessionId, history, petState, petConfig, ct, source, outputChannel);

        await foreach (var item in outputChannel.Reader.ReadAllAsync(ct))
            yield return item;

        // 确保后台任务的异常被观察到
        try { await execution; }
        catch (OperationCanceledException) { /* 取消时静默 */ }
    }

    /// <summary>
    /// Pet 编排核心逻辑（非迭代器，可自由使用 try-catch）。
    /// 通过 Channel 将 StreamItem 传递给迭代器包装层。
    /// </summary>
    private async Task ExecuteWithPetAsync(
        string sessionId,
        IReadOnlyList<SessionMessage> history,
        PetState petState,
        PetConfig petConfig,
        CancellationToken ct,
        string source,
        Channel<StreamItem> output)
    {
        PetDispatchResult dispatch = new() { Reason = "初始化" };
        bool messageSucceeded = false;

        try
        {
            // ── 2. 更新 Pet 状态为 Dispatching ──
            var dispatchingState = petState with
            {
                BehaviorState = PetBehaviorState.Dispatching,
                UpdatedAt = DateTimeOffset.UtcNow,
            };
            await _stateStore.SaveAsync(dispatchingState, ct);

            // ── 3. Pet RAG 检索（用户消息相关知识）──
            string? petRagKnowledge = null;
            string userMessage = ExtractUserMessage(history);
            if (!string.IsNullOrWhiteSpace(userMessage))
            {
                try
                {
                    string ragResult = await _petRagScope.QueryAsync(userMessage, sessionId, topK: 3, ct);
                    if (!string.IsNullOrWhiteSpace(ragResult))
                        petRagKnowledge = ragResult;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Pet [{SessionId}] RAG 检索失败，跳过", sessionId);
                }
            }

            // ── 4. 构建决策上下文并调用 PetDecisionEngine(LLM) ──
            var emotion = await _emotionStore.GetCurrentAsync(sessionId, ct);
            var rateLimitStatus = await _rateLimiter.GetStatusAsync(sessionId, ct);
            var recentSummaries = BuildRecentSummaries(history, maxCount: 10);
            var agentSummaries = BuildAgentSummaries(petConfig);
            var providerSummaries = BuildProviderSummaries();
            var toolGroups = BuildToolGroupNames();

            var context = new PetDecisionContext
            {
                UserMessage = userMessage,
                RecentMessageSummaries = recentSummaries,
                AvailableAgents = agentSummaries,
                AvailableProviders = providerSummaries,
                AvailableToolGroups = toolGroups,
                BehaviorState = petState.BehaviorState,
                EmotionState = emotion,
                RateLimitStatus = rateLimitStatus,
                PetRagKnowledge = petRagKnowledge,
            };

            try
            {
                dispatch = await _decisionEngine.DecideAsync(context, petConfig.PreferredProviderId, sessionId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Pet [{SessionId}] DecisionEngine 调用失败，回退默认", sessionId);
                dispatch = new PetDispatchResult { Reason = $"决策失败: {ex.Message}" };
            }

            _logger.LogInformation(
                "Pet [{SessionId}] 调度决策: Agent={AgentId}, Provider={ProviderId}, ShouldPetRespond={PetRespond}, Reason={Reason}",
                sessionId, dispatch.AgentId ?? "default", dispatch.ProviderId ?? "default",
                dispatch.ShouldPetRespond, dispatch.Reason);

            // ── 5. 根据 dispatch 结果执行 ──
            if (dispatch.ShouldPetRespond && !string.IsNullOrWhiteSpace(dispatch.PetResponse))
            {
                // Pet 自己直接回复（不走 Agent）
                output.Writer.TryWrite(new TokenItem(dispatch.PetResponse));
                messageSucceeded = true;
            }
            else
            {
                // 委派 AgentRunner 执行
                await foreach (var item in DelegateToAgentRunnerAsync(
                    sessionId, history, dispatch, petConfig, emotion, ct, source))
                {
                    output.Writer.TryWrite(item);
                }
                messageSucceeded = true;
            }

            output.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            if (ex is not OperationCanceledException)
                _logger.LogError(ex, "Pet [{SessionId}] 执行失败", sessionId);
            output.Writer.TryComplete(ex);
        }
        finally
        {
            // ── 6. 后处理：更新情绪、恢复状态、记录 journal、收集习惯 ──
            await PostProcessAsync(sessionId, petState.BehaviorState, messageSucceeded, dispatch, ct);
        }
    }

    // ── IAgentMessageHandler ────────────────────────────────────────────────

    /// <summary>所有渠道消息默认路由到主 Agent（IsDefault=true），不再按渠道绑定匹配。</summary>
    public bool HasAgentForChannel(string channelId)
    {
        AgentConfig? main = _agentStore.GetDefault();
        return main is { IsEnabled: true };
    }

    /// <summary>
    /// 渠道消息入口：通过 PetRunner 编排，内部调用 AgentRunner。
    /// </summary>
    public IAsyncEnumerable<StreamItem> HandleMessageAsync(
        string channelId,
        string sessionId,
        IReadOnlyList<SessionMessage> history,
        CancellationToken ct = default) =>
        HandleMessageAsync(sessionId, history, ct, source: "channel", channelId: channelId);

    // ── 内部辅助方法 ─────────────────────────────────────────────────────

    /// <summary>Pet 未启用时，直接透传 AgentRunner（保持原有 Web chat 行为）。</summary>
    private async IAsyncEnumerable<StreamItem> PassthroughToAgentRunnerAsync(
        string sessionId,
        IReadOnlyList<SessionMessage> history,
        [EnumeratorCancellation] CancellationToken ct,
        string source)
    {
        SessionInfo? session = _sessionReader.Get(sessionId);
        string providerId = session?.ProviderId ?? string.Empty;

        AgentConfig? agent = ResolveAgent(session?.AgentId);
        if (agent is null || !agent.IsEnabled)
            throw new InvalidOperationException("No enabled agent found for this session.");

        await foreach (var item in _agentRunner.StreamReActAsync(agent, providerId, history, sessionId, ct, source))
            yield return item;
    }

    /// <summary>根据 PetDispatchResult 委派 AgentRunner 执行。</summary>
    private async IAsyncEnumerable<StreamItem> DelegateToAgentRunnerAsync(
        string sessionId,
        IReadOnlyList<SessionMessage> history,
        PetDispatchResult dispatch,
        PetConfig petConfig,
        EmotionState emotion,
        [EnumeratorCancellation] CancellationToken ct,
        string source)
    {
        SessionInfo? session = _sessionReader.Get(sessionId);

        // 解析 Agent：dispatch 指定 > Session 绑定 > 默认
        AgentConfig? agent = !string.IsNullOrWhiteSpace(dispatch.AgentId)
            ? _agentStore.GetById(dispatch.AgentId) ?? ResolveAgent(session?.AgentId)
            : ResolveAgent(session?.AgentId);
        if (agent is null || !agent.IsEnabled)
            throw new InvalidOperationException("No enabled agent found for this session.");

        // 解析 Provider：dispatch 指定 > Session 绑定
        string providerId = !string.IsNullOrWhiteSpace(dispatch.ProviderId)
            ? dispatch.ProviderId
            : session?.ProviderId ?? string.Empty;

        // 构建 PetOverrides（情绪 + 工具覆盖 + Pet 知识）
        var behaviorProfile = _emotionBehaviorMapper.GetProfile(emotion);
        var petOverrides = new PetOverrides
        {
            Temperature = behaviorProfile.Temperature,
            TopP = behaviorProfile.TopP,
            BehaviorSuffix = behaviorProfile.SystemPromptSuffix,
            ToolOverrides = dispatch.ToolOverrides is { Count: > 0 } ? dispatch.ToolOverrides : null,
            PetKnowledge = dispatch.PetKnowledge,
        };

        await foreach (var item in _agentRunner.StreamReActAsync(
            agent, providerId, history, sessionId, ct, source, petOverrides))
        {
            yield return item;
        }
    }

    /// <summary>后处理：更新情绪 + 恢复 Pet 状态 + 记录 journal + 收集习惯。</summary>
    private async Task PostProcessAsync(
        string sessionId,
        PetBehaviorState previousState,
        bool messageSucceeded,
        PetDispatchResult dispatch,
        CancellationToken ct)
    {
        try
        {
            // 更新情绪
            var emotionEvent = messageSucceeded
                ? EmotionEventType.MessageSuccess
                : EmotionEventType.MessageFailed;
            var currentEmotion = await _emotionStore.GetCurrentAsync(sessionId, ct);
            var newEmotion = _emotionRuleEngine.Evaluate(currentEmotion, emotionEvent);
            await _emotionStore.SaveAsync(sessionId, newEmotion, ct);

            // 恢复 Pet 状态（从 Dispatching 回到之前状态，通常是 Idle）
            var petState = await _stateStore.LoadAsync(sessionId, ct);
            if (petState is not null && petState.BehaviorState == PetBehaviorState.Dispatching)
            {
                var restoredState = petState with
                {
                    BehaviorState = previousState == PetBehaviorState.Dispatching
                        ? PetBehaviorState.Idle // 避免无限 Dispatching
                        : previousState,
                    UpdatedAt = DateTimeOffset.UtcNow,
                };
                await _stateStore.SaveAsync(restoredState, ct);
            }

            // 记录 journal
            string detail = $"success={messageSucceeded}, agent={dispatch.AgentId ?? "default"}, " +
                            $"provider={dispatch.ProviderId ?? "default"}, petRespond={dispatch.ShouldPetRespond}";
            await _stateStore.AppendJournalAsync(sessionId, "message_handled", detail, ct);

            // fire-and-forget 收集习惯
            _ = _sessionObserver.ObserveMessageAsync(sessionId, dispatch, messageSucceeded);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pet [{SessionId}] 后处理失败（不影响用户）", sessionId);
        }
    }

    /// <summary>从消息历史提取最新用户消息内容。</summary>
    private static string ExtractUserMessage(IReadOnlyList<SessionMessage> history)
    {
        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Role == "user")
                return history[i].Content;
        }
        return string.Empty;
    }

    /// <summary>构建最近消息摘要。</summary>
    private static IReadOnlyList<string> BuildRecentSummaries(IReadOnlyList<SessionMessage> history, int maxCount)
    {
        int start = Math.Max(0, history.Count - maxCount);
        var summaries = new List<string>(Math.Min(maxCount, history.Count));
        for (int i = start; i < history.Count; i++)
        {
            var msg = history[i];
            string snippet = msg.Content.Length > 100 ? msg.Content[..100] + "..." : msg.Content;
            summaries.Add($"[{msg.Role}] {snippet}");
        }
        return summaries;
    }

    /// <summary>构建可用 Agent 摘要列表（尊重 PetConfig.AllowedAgentIds 约束）。</summary>
    private IReadOnlyList<AgentSummary> BuildAgentSummaries(PetConfig config)
    {
        var agents = _agentStore.All.Where(a => a.IsEnabled);
        if (config.AllowedAgentIds is { Count: > 0 })
        {
            var allowed = new HashSet<string>(config.AllowedAgentIds);
            agents = agents.Where(a => allowed.Contains(a.Id));
        }
        return agents.Select(a => new AgentSummary(a.Id, a.Name, a.Description, a.IsDefault)).ToList();
    }

    /// <summary>构建可用 Provider 摘要列表（仅 Chat 类型）。</summary>
    private IReadOnlyList<ProviderSummary> BuildProviderSummaries()
    {
        return _providerStore.All
            .Where(p => p.IsEnabled && p.ModelType != ModelType.Embedding)
            .Select(p => new ProviderSummary(
                p.Id, p.DisplayName, p.ModelName,
                p.Capabilities.QualityScore, p.Capabilities.LatencyTier.ToString(),
                p.Capabilities.InputPricePerMToken, p.Capabilities.OutputPricePerMToken,
                p.IsDefault))
            .ToList();
    }

    /// <summary>构建工具组名称列表。</summary>
    private static IReadOnlyList<string> BuildToolGroupNames()
    {
        // 工具组列表目前由静态类型定义，简化返回常见内置工具组
        return ["fetch", "shell", "cron", "sub-agent", "file", "skill"];
    }

    /// <summary>解析 Agent：优先 agentId，回退默认。</summary>
    private AgentConfig? ResolveAgent(string? agentId) =>
        string.IsNullOrWhiteSpace(agentId)
            ? _agentStore.GetDefault()
            : _agentStore.GetById(agentId) ?? _agentStore.GetDefault();
}
