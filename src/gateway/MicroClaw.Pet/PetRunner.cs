using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MicroClaw.Agent;
using AgentEntity = MicroClaw.Agent.Agent;
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
/// Pet 会话编排引擎：所有用户消息先经 Pet，由 Pet（LLM）决策选择模型/Agent/工具，
/// 再委派 <see cref="AgentRunner"/> 执行。
/// <para>
/// HandleMessageAsync 流程：
/// <list type="number">
///   <item>从 ISessionRepository 获取 Session，取 Per-Session PetContext（懒加载）</item>
///   <item>若 Pet 未启用，直接透传 AgentRunner</item>
///   <item>子代理会话（ParentSessionId != null）使用根会话的 PetContext（O-3-7）</item>
///   <item>Pet RAG 检索（用户消息相关知识）</item>
///   <item>PetDecisionEngine(LLM) 调度决策</item>
///   <item>根据 dispatch 结果调用 AgentRunner.StreamReActAsync()（或 Pet 直接回复）</item>
///   <item>后处理：通过 PetContext 更新情绪/状态，记录 journal，收集习惯</item>
/// </list>
/// 实现 <see cref="IAgentMessageHandler"/> 供渠道消息处理器路由调用。
/// </para>
/// </summary>
public sealed class PetRunner(
    AgentRunner agentRunner,
    AgentStore agentStore,
    ISessionRepository sessionRepo,
    PetContextFactory petContextFactory,
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
    private readonly ISessionRepository _sessionRepo = sessionRepo ?? throw new ArgumentNullException(nameof(sessionRepo));
    private readonly PetContextFactory _petContextFactory = petContextFactory ?? throw new ArgumentNullException(nameof(petContextFactory));
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

        // ── 1. 获取 Session（用于路由：ProviderId / AgentId）──
        Session? session = _sessionRepo.Get(sessionId);

        // ── 2. 子代理会话：使用根会话的 PetContext（O-3-7）──
        // 子代理会话不拥有独立 Pet，共享根会话的 Pet 编排上下文。
        Session? petSession = session;
        if (session?.ParentSessionId is not null)
        {
            string rootId = _sessionRepo.GetRootSessionId(sessionId);
            petSession = _sessionRepo.Get(rootId);
        }

        // ── 3. 获取 Pet（懒加载：服务重启后 Pet 为 null）──
        PetContext? petCtx = petSession?.Pet as PetContext;
        if (petCtx is null && petSession is { IsApproved: true })
        {
            var loaded = await _petContextFactory.LoadAsync(petSession.Id, ct);
            if (loaded is not null)
            {
                petSession.AttachPet(loaded);
                petCtx = loaded;
            }
        }

        // ── 4. Pet 未启用，直接透传 AgentRunner ──
        if (petCtx is null || !petCtx.IsEnabled)
        {
            _logger.LogDebug("Pet 未启用 (SessionId={SessionId})，透传 AgentRunner", sessionId);
            await foreach (var item in PassthroughToAgentRunnerAsync(session, history, ct, source))
                yield return item;
            yield break;
        }

        // ── 薄迭代器：C# 不允许在含 catch 或 try 块中 yield，通过 Channel 解耦 ──
        var outputChannel = Channel.CreateUnbounded<StreamItem>();
        Task execution = ExecuteWithPetAsync(sessionId, session, history, petCtx, ct, source, outputChannel);

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
        Session? session,
        IReadOnlyList<SessionMessage> history,
        PetContext petCtx,
        CancellationToken ct,
        string source,
        Channel<StreamItem> output)
    {
        var petConfig = petCtx.Config;
        PetDispatchResult dispatch = new() { Reason = "初始化" };
        bool messageSucceeded = false;
        // 保存进入 Dispatching 前的状态，后处理时恢复
        PetBehaviorState previousBehaviorState = petCtx.PetState.BehaviorState;

        try
        {
            // ── 2. 更新 Pet 状态为 Dispatching ──
            petCtx.UpdateBehaviorState(PetBehaviorState.Dispatching);
            await _stateStore.SaveAsync(petCtx.PetState, ct);

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
            // 直接使用 PetContext 中的内存情绪快照，避免重复 I/O
            var emotion = petCtx.Emotion;
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
                BehaviorState = previousBehaviorState,
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
                // Pet 自己直接回复（不经 Agent）
                output.Writer.TryWrite(new TokenItem(dispatch.PetResponse));
                messageSucceeded = true;
            }
            else
            {
                // 委派 AgentRunner 执行
                await foreach (var item in DelegateToAgentRunnerAsync(
                    sessionId, session, history, dispatch, petCtx, ct, source))
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
            // ── 6. 后处理：通过 PetContext 更新情绪/状态，记录 journal，收集习惯 ──
            await PostProcessAsync(sessionId, petCtx, previousBehaviorState, messageSucceeded, dispatch, ct);
        }
    }

    // ── IAgentMessageHandler ────────────────────────────────────────────────

    /// <summary>所有渠道消息默认路由到�?Agent（IsDefault=true），不再按渠道绑定匹配�?/summary>
    public bool HasAgentForChannel(string channelId)
    {
        AgentEntity? main = _agentStore.GetDefaultAgent();
        return main is { IsEnabled: true };
    }

    /// <summary>
    /// 渠道消息入口：通过 PetRunner 编排，内部调�?AgentRunner�?
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
        Session? session,
        IReadOnlyList<SessionMessage> history,
        [EnumeratorCancellation] CancellationToken ct,
        string source)
    {
        string providerId = session?.ProviderId ?? string.Empty;

        AgentEntity? agent = ResolveAgent(session?.AgentId);
        if (agent is null || !agent.IsEnabled)
            throw new InvalidOperationException("No enabled agent found for this session.");

        await foreach (var item in _agentRunner.StreamReActAsync(agent, providerId, history, session?.Id ?? string.Empty, ct, source))
            yield return item;
    }

    /// <summary>根据 PetDispatchResult 委派 AgentRunner 执行。</summary>
    private async IAsyncEnumerable<StreamItem> DelegateToAgentRunnerAsync(
        string sessionId,
        Session? session,
        IReadOnlyList<SessionMessage> history,
        PetDispatchResult dispatch,
        PetContext petCtx,
        [EnumeratorCancellation] CancellationToken ct,
        string source)
    {
        // 解析 Agent：dispatch 指定 > Session 绑定 > 默认
        AgentEntity? agent = !string.IsNullOrWhiteSpace(dispatch.AgentId)
            ? _agentStore.GetAgentById(dispatch.AgentId) ?? ResolveAgent(session?.AgentId)
            : ResolveAgent(session?.AgentId);
        if (agent is null || !agent.IsEnabled)
            throw new InvalidOperationException("No enabled agent found for this session.");

        // 解析 Provider：dispatch 指定 > Session 绑定
        string providerId = !string.IsNullOrWhiteSpace(dispatch.ProviderId)
            ? dispatch.ProviderId
            : session?.ProviderId ?? string.Empty;

        // 构建 PetOverrides（情绪参数 + 工具覆盖 + Pet 知识）
        var behaviorProfile = _emotionBehaviorMapper.GetProfile(petCtx.Emotion);
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

    /// <summary>后处理：通过 PetContext 更新情绪/状态 + 持久化，记录 journal，收集习惯。</summary>
    private async Task PostProcessAsync(
        string sessionId,
        PetContext petCtx,
        PetBehaviorState previousState,
        bool messageSucceeded,
        PetDispatchResult dispatch,
        CancellationToken ct)
    {
        try
        {
            // 通过 PetContext 更新情绪（应用情绪增减量）
            var emotionEvent = messageSucceeded
                ? EmotionEventType.MessageSuccess
                : EmotionEventType.MessageFailed;
            var delta = _emotionRuleEngine.GetDelta(emotionEvent);
            petCtx.UpdateEmotion(delta);
            await _emotionStore.SaveAsync(sessionId, petCtx.Emotion, ct);

            // 通过 PetContext 恢复 Pet 状态（从 Dispatching 回到之前状态，通常是 Idle）
            if (petCtx.PetState.BehaviorState == PetBehaviorState.Dispatching)
            {
                petCtx.UpdateBehaviorState(previousState == PetBehaviorState.Dispatching
                    ? PetBehaviorState.Idle // 避免无限 Dispatching
                    : previousState);
            }
            await _stateStore.SaveAsync(petCtx.PetState, ct);
            petCtx.ClearDirty();

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

    /// <summary>从消息历史提取最新用户消息内容�?/summary>
    private static string ExtractUserMessage(IReadOnlyList<SessionMessage> history)
    {
        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Role == "user")
                return history[i].Content;
        }
        return string.Empty;
    }

    /// <summary>构建最近消息摘要�?/summary>
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

    /// <summary>构建可用 Agent 摘要列表（尊�?PetConfig.AllowedAgentIds 约束）�?/summary>
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

    /// <summary>构建可用 Provider 摘要列表（仅 Chat 类型）�?/summary>
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

    /// <summary>构建工具组名称列表�?/summary>
    private static IReadOnlyList<string> BuildToolGroupNames()
    {
        // 工具组列表目前由静态类型定义，简化返回常见内置工具组
        return ["fetch", "shell", "cron", "sub-agent", "file", "skill"];
    }

    /// <summary>解析 Agent：优�?agentId，回退默认�?/summary>
    private AgentEntity? ResolveAgent(string? agentId) =>
        string.IsNullOrWhiteSpace(agentId)
            ? _agentStore.GetDefaultAgent()
            : _agentStore.GetAgentById(agentId) ?? _agentStore.GetDefaultAgent();
}


