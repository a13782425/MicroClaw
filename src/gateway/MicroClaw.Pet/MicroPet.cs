using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Agent;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Observer;
using MicroClaw.Pet.Rag;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet;

/// <summary>
/// Per-Session Pet 上下文的生命周期状态。
/// </summary>
internal enum PetContextState
{
    /// <summary>尚未初始化（Pet 目录未创建、或 PetFactory 尚未调用前的默认状态）。</summary>
    Uninitialized,

    /// <summary>已激活，正常参与消息编排。</summary>
    Active,

    /// <summary>已禁用（Session 被禁用，或 PetConfig.Enabled = false）。</summary>
    Disabled,
}

/// <summary>
/// Per-Session Pet 上下文的具体实现（<see cref="IPet"/>）。
/// <para>
/// 作为纯状态持有者（Pure State Holder），持有当前会话 Pet 的三类状态快照：
/// <list type="bullet">
///   <item><see cref="PetState"/> — 行为状态（BehaviorState）与运行时快照</item>
///   <item><see cref="Config"/> — Per-Session Pet 配置（Enabled / 限流 / 允许 Agent 列表等）</item>
///   <item><see cref="Emotion"/> — 四维情绪快照</item>
/// </list>
/// </para>
/// <para>
/// 生命周期：由 <see cref="PetContextFactory"/> 在审批时创建（或在首次使用时懒加载），
/// 通过 <c>Session.AttachPet(petCtx)</c> 挂载到会话上。
/// 会话删除时由 <see cref="IDisposable.Dispose"/> 标记失效。
/// </para>
/// <para>
/// 线程安全注意：<see cref="MarkDirty"/> 使用 volatile 保证可见性，
/// 但 <see cref="UpdateEmotion"/> / <see cref="UpdateBehaviorState"/> 不保证原子性，
/// 调用方应确保单一会话的并发写入通过上层协调（例如消息队列）串行化。
/// </para>
/// </summary>
public sealed class MicroPet : IPet, IDisposable
{
    private PetState _petState;
    private volatile bool _isDirty;
    private bool _disposed;

    // ── Subsystem references (internal, not exposed) ──────────────────────
    private readonly PetDecisionEngine _decisionEngine;
    private readonly IEmotionStore _emotionStore;
    private readonly IEmotionRuleEngine _emotionRuleEngine;
    private readonly IEmotionBehaviorMapper _emotionBehaviorMapper;
    private readonly PetRagScope _petRagScope;
    private readonly PetStateStore _stateStore;
    private readonly PetSessionObserver _sessionObserver;
    private readonly PetRateLimiter _rateLimiter;
    private readonly PetSelfAwarenessReportBuilder _reportBuilder;
    private readonly AgentStore _agentStore;
    private readonly ProviderConfigStore _providerStore;
    private readonly ILogger _logger;

    internal MicroPet(
        IMicroSession microSession,
        PetState state,
        PetConfig config,
        EmotionState emotion,
        PetContextState initialState,
        PetDecisionEngine decisionEngine,
        IEmotionStore emotionStore,
        IEmotionRuleEngine emotionRuleEngine,
        IEmotionBehaviorMapper emotionBehaviorMapper,
        PetRagScope petRagScope,
        PetStateStore stateStore,
        PetSessionObserver sessionObserver,
        PetRateLimiter rateLimiter,
        PetSelfAwarenessReportBuilder reportBuilder,
        AgentStore agentStore,
        ProviderConfigStore providerStore,
        ILoggerFactory loggerFactory)
    {
        MicroSession = microSession ?? throw new ArgumentNullException(nameof(microSession));
        _petState = state ?? throw new ArgumentNullException(nameof(state));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Emotion = emotion;
        State = initialState;

        _decisionEngine = decisionEngine ?? throw new ArgumentNullException(nameof(decisionEngine));
        _emotionStore = emotionStore ?? throw new ArgumentNullException(nameof(emotionStore));
        _emotionRuleEngine = emotionRuleEngine ?? throw new ArgumentNullException(nameof(emotionRuleEngine));
        _emotionBehaviorMapper = emotionBehaviorMapper ?? throw new ArgumentNullException(nameof(emotionBehaviorMapper));
        _petRagScope = petRagScope ?? throw new ArgumentNullException(nameof(petRagScope));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _sessionObserver = sessionObserver ?? throw new ArgumentNullException(nameof(sessionObserver));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _reportBuilder = reportBuilder ?? throw new ArgumentNullException(nameof(reportBuilder));
        _agentStore = agentStore ?? throw new ArgumentNullException(nameof(agentStore));
        _providerStore = providerStore ?? throw new ArgumentNullException(nameof(providerStore));
        _logger = loggerFactory?.CreateLogger<MicroPet>() ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    // ── IPet ──────────────────────────────────────────────────────────────

    /// <summary>当前 Pet 生命周期状态。</summary>
    internal PetContextState State { get; private set; }

    /// <inheritdoc/>
    public IMicroSession MicroSession { get; }

    /// <inheritdoc/>
    public bool IsEnabled => !_disposed && State == PetContextState.Active && Config.Enabled;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Microsoft.Extensions.AI.AIFunction>> CollectChannelToolsAsync(CancellationToken cancellationToken = default)
    {
        return MicroSession.Channel is { } channel
            ? await channel.CreateToolsAsync(cancellationToken)
            : [];
    }

    /// <inheritdoc/>
    public void MarkDirty() => _isDirty = true;

    // ── 状态数据（只读外部，内部可变）────────────────────────────────────────

    /// <summary>当前 Pet 行为状态快照（不可变 record）。</summary>
    public PetState PetState => _petState;

    /// <summary>Per-Session Pet 配置（可变 class，外部可直接修改属性后持久化）。</summary>
    public PetConfig Config { get; }

    /// <summary>当前四维情绪状态快照（不可变 record）。</summary>
    public EmotionState Emotion { get; private set; }

    /// <summary>是否有待持久化的未保存变更。</summary>
    public bool IsDirty => _isDirty;

    // ── O-3-2: 状态操作方法 ───────────────────────────────────────────────────

    /// <summary>
    /// 将情绪增减量 <paramref name="delta"/> 应用到当前情绪状态，更新内存快照并标记 Dirty。
    /// </summary>
    public void UpdateEmotion(EmotionDelta delta)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Emotion = Emotion.Apply(delta);
        MarkDirty();
    }

    /// <summary>
    /// 将当前情绪直接替换为 <paramref name="newEmotion"/>，更新内存快照并标记 Dirty。
    /// </summary>
    public void UpdateEmotion(EmotionState newEmotion)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Emotion = newEmotion ?? throw new ArgumentNullException(nameof(newEmotion));
        MarkDirty();
    }

    /// <summary>
    /// 更新 Pet 行为状态，记录变更时间并标记 Dirty。
    /// </summary>
    public void UpdateBehaviorState(PetBehaviorState newBehaviorState)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _petState = _petState with
        {
            BehaviorState = newBehaviorState,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        MarkDirty();
    }

    /// <summary>
    /// 清除脏标记（由持久化层在写盘后调用）。
    /// </summary>
    public void ClearDirty() => _isDirty = false;

    /// <summary>
    /// Activates the current PetContext without replacing the runtime object.
    /// </summary>
    public void Activate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        State = PetContextState.Active;
    }

    // ── Pet 编排方法（internal，由 PetRunner 调用）──────────────────────────

    /// <summary>
    /// Enter Dispatching state and persist. Returns the previous behavior state for later restoration.
    /// </summary>
    internal async Task<PetBehaviorState> EnterDispatchingAsync(CancellationToken ct = default)
    {
        PetBehaviorState previous = PetState.BehaviorState;
        UpdateBehaviorState(PetBehaviorState.Dispatching);
        await _stateStore.SaveAsync(PetState, ct);
        return previous;
    }

    /// <summary>
    /// Pet LLM 决策：RAG 检索 → 构建 PetDecisionContext → 委托 PetDecisionEngine 决策。
    /// </summary>
    /// <param name="history">当前会话消息历史。</param>
    /// <param name="previousBehaviorState">进入 Dispatching 前的行为状态。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>调度决策结果。</returns>
    internal async Task<PetDispatchResult> DecideAsync(
        IReadOnlyList<SessionMessage> history,
        PetBehaviorState previousBehaviorState,
        CancellationToken ct = default)
    {
        string sessionId = MicroSession.Id;

        // ── RAG 检索 ──
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

        // ── 构建决策上下文 ──
        var rateLimitStatus = await _rateLimiter.GetStatusAsync(sessionId, ct);
        var recentSummaries = BuildRecentSummaries(history, maxCount: 10);
        var agentSummaries = BuildAgentSummaries(Config);
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
            EmotionState = Emotion,
            RateLimitStatus = rateLimitStatus,
            PetRagKnowledge = petRagKnowledge,
        };

        // ── 委托 DecisionEngine ──
        try
        {
            return await _decisionEngine.DecideAsync(context, Config.PreferredProviderId, sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Pet [{SessionId}] DecisionEngine 调用失败，回退默认", sessionId);
            return new PetDispatchResult { Reason = $"决策失败: {ex.Message}" };
        }
    }

    /// <summary>
    /// 后处理：通过情绪引擎更新情绪/状态 + 持久化，记录 journal，收集习惯。
    /// </summary>
    internal async Task PostProcessAsync(
        PetBehaviorState previousState,
        bool messageSucceeded,
        PetDispatchResult dispatch,
        CancellationToken ct = default)
    {
        string sessionId = MicroSession.Id;

        try
        {
            // 通过情绪规则引擎更新情绪（应用情绪增减量）
            var emotionEvent = messageSucceeded
                ? EmotionEventType.MessageSuccess
                : EmotionEventType.MessageFailed;
            var delta = _emotionRuleEngine.GetDelta(emotionEvent);
            UpdateEmotion(delta);
            await _emotionStore.SaveAsync(sessionId, Emotion, ct);

            // 恢复 Pet 状态（从 Dispatching 回到之前状态，通常是 Idle）
            if (PetState.BehaviorState == PetBehaviorState.Dispatching)
            {
                UpdateBehaviorState(previousState == PetBehaviorState.Dispatching
                    ? PetBehaviorState.Idle
                    : previousState);
            }
            await _stateStore.SaveAsync(PetState, ct);
            ClearDirty();

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

    /// <summary>
    /// Get the <see cref="BehaviorProfile"/> mapped from current emotion state.
    /// Used by PetRunner to build <see cref="PetOverrides"/>.
    /// </summary>
    internal BehaviorProfile GetBehaviorProfile() => _emotionBehaviorMapper.GetProfile(Emotion);

    // ── 决策辅助方法 ──────────────────────────────────────────────────────

    private static string ExtractUserMessage(IReadOnlyList<SessionMessage> history)
    {
        for (int i = history.Count - 1; i >= 0; i--)
        {
            if (history[i].Role == "user")
                return history[i].Content;
        }
        return string.Empty;
    }

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

    private static IReadOnlyList<string> BuildToolGroupNames()
    {
        return ["fetch", "shell", "cron", "sub-agent", "file", "skill"];
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    /// <summary>
    /// 将 PetContext 标记为已释放（Disabled）。
    /// 会话删除时由 <see cref="SessionDeletedEventHandler"/> 调用，
    /// 防止此后 PetRunner 继续使用已失效的状态。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        State = PetContextState.Disabled;
    }
}
