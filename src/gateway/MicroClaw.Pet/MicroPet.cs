using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MicroClaw.Abstractions.Pet;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Agent;
using AgentEntity = MicroClaw.Agent.Agent;
using MicroClaw.Pet.Decision;
using MicroClaw.Pet.Emotion;
using MicroClaw.Pet.Observer;
using MicroClaw.Pet.Rag;
using MicroClaw.Pet.RateLimit;
using MicroClaw.Pet.StateMachine;
using MicroClaw.Pet.Storage;
using MicroClaw.Providers;
using MicroClaw.Tools;
using MicroClaw.Utils;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
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
/// 作为 <see cref="MicroClaw.Core.MicroObject"/>，在会话删除时由外层调用
/// <see cref="System.IAsyncDisposable.DisposeAsync"/> 进入
/// <see cref="OnDisposedAsync(System.Threading.CancellationToken)"/> 将 <see cref="State"/> 标记为
/// <see cref="PetContextState.Disabled"/> 并同步释放已挂载的 <see cref="PetComponent"/>。
/// </para>
/// <para>
/// 线程安全注意：<see cref="MarkDirty"/> 使用 volatile 保证可见性，
/// 但 <see cref="UpdateEmotion"/> / <see cref="UpdateBehaviorState"/> 不保证原子性，
/// 调用方应确保单一会话的并发写入通过上层协调（例如消息队列）串行化。
/// </para>
/// </summary>
public sealed class MicroPet : MicroClaw.Core.MicroObject, IPet
{
    private PetState _petState;
    private volatile bool _isDirty;

    // ── Subsystem references (resolved from IServiceProvider) ─────────────
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
    private readonly ProviderService _providerStore;
    private readonly ISessionService _sessionService;
    private readonly AgentRunner _agentRunner;
    private readonly ToolCollector _toolCollector;
    private readonly ILogger _logger;

    internal MicroPet(
        IServiceProvider sp,
        IMicroSession microSession,
        PetState state,
        PetConfig config,
        EmotionState emotion,
        PetContextState initialState)
    {
        ArgumentNullException.ThrowIfNull(sp);
        MicroSession = microSession ?? throw new ArgumentNullException(nameof(microSession));
        _petState = state ?? throw new ArgumentNullException(nameof(state));
        Config = config ?? throw new ArgumentNullException(nameof(config));
        Emotion = emotion;
        State = initialState;

        _decisionEngine = sp.GetRequiredService<PetDecisionEngine>();
        _emotionStore = sp.GetRequiredService<IEmotionStore>();
        _emotionRuleEngine = sp.GetRequiredService<IEmotionRuleEngine>();
        _emotionBehaviorMapper = sp.GetRequiredService<IEmotionBehaviorMapper>();
        _petRagScope = sp.GetRequiredService<PetRagScope>();
        _stateStore = sp.GetRequiredService<PetStateStore>();
        _sessionObserver = sp.GetRequiredService<PetSessionObserver>();
        _rateLimiter = sp.GetRequiredService<PetRateLimiter>();
        _reportBuilder = sp.GetRequiredService<PetSelfAwarenessReportBuilder>();
        _agentStore = sp.GetRequiredService<AgentStore>();
        _providerStore = sp.GetRequiredService<ProviderService>();
        _sessionService = sp.GetRequiredService<ISessionService>();
        _agentRunner = sp.GetRequiredService<AgentRunner>();
        _toolCollector = sp.GetRequiredService<ToolCollector>();
        _logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<MicroPet>();
    }

    // ── IPet ──────────────────────────────────────────────────────────────

    /// <summary>当前 Pet 生命周期状态。</summary>
    internal PetContextState State { get; private set; }

    /// <inheritdoc/>
    public IMicroSession MicroSession { get; }

    /// <inheritdoc/>
    public bool IsEnabled => !IsDisposed && State == PetContextState.Active && Config.Enabled;

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
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Emotion = Emotion.Apply(delta);
        MarkDirty();
    }

    /// <summary>
    /// 将当前情绪直接替换为 <paramref name="newEmotion"/>，更新内存快照并标记 Dirty。
    /// </summary>
    public void UpdateEmotion(EmotionState newEmotion)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Emotion = newEmotion ?? throw new ArgumentNullException(nameof(newEmotion));
        MarkDirty();
    }

    /// <summary>
    /// 更新 Pet 行为状态，记录变更时间并标记 Dirty。
    /// </summary>
    public void UpdateBehaviorState(PetBehaviorState newBehaviorState)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
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
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        State = PetContextState.Active;
    }

    // ── IPet.HandleChatAsync — 完整消息处理 ─────────────────────────────────

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamItem> HandleChatAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string sessionId = MicroSession.Id;

        // ── 1. 保存用户消息 ──
        SessionMessage userMessage = new(
            Id: MicroClawUtils.GetUniqueId(),
            Role: "user",
            Content: request.Content,
            ThinkContent: null,
            Timestamp: DateTimeOffset.UtcNow,
            Attachments: request.Attachments);
        _sessionService.AddMessage(sessionId, userMessage);

        // ── 2. 加载历史消息 ──
        IReadOnlyList<SessionMessage> history = _sessionService.GetMessages(sessionId);

        // ── 3. Pet 未启用：直接透传 AgentRunner ──
        if (!IsEnabled)
        {
            _logger.LogDebug("Pet 未启用 (SessionId={SessionId})，透传 AgentRunner", sessionId);

            AgentEntity? agent = ResolveAgent(MicroSession.AgentId);
            if (agent is null || !agent.IsEnabled)
                throw new InvalidOperationException("No enabled agent found for this session.");

            string providerId = MicroSession.ProviderId;

            var disabledBranchCtx = new ChatContext
            {
                Session = MicroSession,
                History = history,
                Source = "chat",
                Output = null,
                Ct = ct,
            };
            await RunPhaseAsync(ChatLifecyclePhase.BeforeChat, disabledBranchCtx);

            var toolContext = BuildToolContext(agent);
            ToolCollectionResult? prebuiltTools = null;
            try
            {
                prebuiltTools = await _toolCollector.CollectToolsAsync(agent, toolContext, ct);
                await foreach (var item in _agentRunner.StreamReActAsync(
                    agent, providerId, history, sessionId, ct, source: "chat", prebuiltTools: prebuiltTools))
                {
                    yield return item;
                }
            }
            finally
            {
                if (prebuiltTools is not null)
                    await prebuiltTools.DisposeAsync();
            }

            // TODO(next-round): 由具体 PetComponent（或 MicroPet 内部消息累加器）把最终 assistant 回复
            // 聚合进 ctx.FinalAssistantMessage。本轮先置 null。
            disabledBranchCtx.FinalAssistantMessage = null;
            await RunPhaseAsync(ChatLifecyclePhase.AfterChat, disabledBranchCtx);
            yield break;
        }

        // ── 4. Pet 启用：Channel 解耦迭代器 ──
        var outputChannel = Channel.CreateUnbounded<StreamItem>();
        var enabledBranchCtx = new ChatContext
        {
            Session = MicroSession,
            History = history,
            Source = "chat",
            Output = outputChannel.Writer,
            Ct = ct,
        };
        await RunPhaseAsync(ChatLifecyclePhase.BeforeChat, enabledBranchCtx);

        Task execution = ExecuteChatWithPetAsync(history, ct, outputChannel, source: "chat");

        await foreach (var item in outputChannel.Reader.ReadAllAsync(ct))
            yield return item;

        try { await execution; }
        catch (OperationCanceledException) { /* cancelled silently */ }

        // TODO(next-round): 累加 token 构造 ctx.FinalAssistantMessage。本轮置 null。
        enabledBranchCtx.FinalAssistantMessage = null;
        await RunPhaseAsync(ChatLifecyclePhase.AfterChat, enabledBranchCtx);
    }

    // ── IPet.HandleMessageAsync — 渠道消息处理（调用方已保存消息 & 加载历史）──

    /// <inheritdoc/>
    public async IAsyncEnumerable<StreamItem> HandleMessageAsync(
        IReadOnlyList<SessionMessage> history,
        [EnumeratorCancellation] CancellationToken ct = default,
        string source = "chat")
    {
        ArgumentNullException.ThrowIfNull(history);
        string sessionId = MicroSession.Id;

        // ── Pet 未启用：直接透传 AgentRunner ──
        if (!IsEnabled)
        {
            _logger.LogDebug("Pet 未启用 (SessionId={SessionId})，透传 AgentRunner (source={Source})", sessionId, source);

            AgentEntity? agent = ResolveAgent(MicroSession.AgentId);
            if (agent is null || !agent.IsEnabled)
                throw new InvalidOperationException("No enabled agent found for this session.");

            string providerId = MicroSession.ProviderId;

            var disabledBranchCtx = new ChatContext
            {
                Session = MicroSession,
                History = history,
                Source = source,
                Output = null,
                Ct = ct,
            };
            await RunPhaseAsync(ChatLifecyclePhase.BeforeChat, disabledBranchCtx);

            var toolContext = BuildToolContext(agent);
            ToolCollectionResult? prebuiltTools = null;
            try
            {
                prebuiltTools = await _toolCollector.CollectToolsAsync(agent, toolContext, ct);
                await foreach (var item in _agentRunner.StreamReActAsync(
                    agent, providerId, history, sessionId, ct, source: source, prebuiltTools: prebuiltTools))
                {
                    yield return item;
                }
            }
            finally
            {
                if (prebuiltTools is not null)
                    await prebuiltTools.DisposeAsync();
            }

            // TODO(next-round): 由具体 PetComponent 聚合 assistant 最终消息。
            disabledBranchCtx.FinalAssistantMessage = null;
            await RunPhaseAsync(ChatLifecyclePhase.AfterChat, disabledBranchCtx);
            yield break;
        }

        // ── Pet 启用：Channel 解耦迭代器 ──
        var outputChannel = Channel.CreateUnbounded<StreamItem>();
        var enabledBranchCtx = new ChatContext
        {
            Session = MicroSession,
            History = history,
            Source = source,
            Output = outputChannel.Writer,
            Ct = ct,
        };
        await RunPhaseAsync(ChatLifecyclePhase.BeforeChat, enabledBranchCtx);

        Task execution = ExecuteChatWithPetAsync(history, ct, outputChannel, source: source);

        await foreach (var item in outputChannel.Reader.ReadAllAsync(ct))
            yield return item;

        try { await execution; }
        catch (OperationCanceledException) { /* cancelled silently */ }

        // TODO(next-round): 累加 token 构造 ctx.FinalAssistantMessage。
        enabledBranchCtx.FinalAssistantMessage = null;
        await RunPhaseAsync(ChatLifecyclePhase.AfterChat, enabledBranchCtx);
    }

    /// <summary>
    /// Pet 编排核心逻辑（非迭代器，可自由使用 try-catch）。
    /// </summary>
    private async Task ExecuteChatWithPetAsync(
        IReadOnlyList<SessionMessage> history,
        CancellationToken ct,
        Channel<StreamItem> output,
        string source = "chat")
    {
        string sessionId = MicroSession.Id;
        PetDispatchResult dispatch = new() { Reason = "初始化" };
        bool messageSucceeded = false;
        PetBehaviorState previousBehaviorState = PetState.BehaviorState;

        try
        {
            // ── Enter Dispatching state ──
            previousBehaviorState = await EnterDispatchingAsync(ct);

            // ── Pet LLM 决策 ──
            dispatch = await DecideAsync(history, previousBehaviorState, ct);

            _logger.LogInformation(
                "Pet [{SessionId}] 调度决策: Agent={AgentId}, Provider={ProviderId}, ShouldPetRespond={PetRespond}, Reason={Reason}",
                sessionId, dispatch.AgentId ?? "default", dispatch.ProviderId ?? "default",
                dispatch.ShouldPetRespond, dispatch.Reason);

            // ── 根据 dispatch 结果执行 ──
            if (dispatch.ShouldPetRespond && !string.IsNullOrWhiteSpace(dispatch.PetResponse))
            {
                output.Writer.TryWrite(new TokenItem(dispatch.PetResponse));
                messageSucceeded = true;
            }
            else
            {
                // Resolve Agent / Provider from dispatch or session defaults
                AgentEntity? agent = !string.IsNullOrWhiteSpace(dispatch.AgentId)
                    ? _agentStore.GetAgentById(dispatch.AgentId) ?? ResolveAgent(MicroSession.AgentId)
                    : ResolveAgent(MicroSession.AgentId);
                if (agent is null || !agent.IsEnabled)
                    throw new InvalidOperationException("No enabled agent found for this session.");

                string providerId = !string.IsNullOrWhiteSpace(dispatch.ProviderId)
                    ? dispatch.ProviderId
                    : MicroSession.ProviderId;

                // Collect tools: common + channel
                var toolContext = BuildToolContext(agent);
                ToolCollectionResult? prebuiltTools = null;
                try
                {
                    prebuiltTools = await _toolCollector.CollectToolsAsync(agent, toolContext, ct);

                    // Merge channel tools
                    IReadOnlyList<AIFunction> channelTools = await CollectChannelToolsAsync(ct);
                    if (channelTools.Count > 0)
                        prebuiltTools.AddTools(channelTools);

                    // Build PetOverrides from emotion-behavior mapping
                    var behaviorProfile = GetBehaviorProfile();
                    var petOverrides = new PetOverrides
                    {
                        Temperature = behaviorProfile.Temperature,
                        TopP = behaviorProfile.TopP,
                        BehaviorSuffix = behaviorProfile.SystemPromptSuffix,
                        ToolOverrides = dispatch.ToolOverrides is { Count: > 0 } ? dispatch.ToolOverrides : null,
                        PetKnowledge = dispatch.PetKnowledge,
                    };

                    await foreach (var item in _agentRunner.StreamReActAsync(
                        agent, providerId, history, sessionId, ct, source: source, petOverrides,
                        prebuiltTools: prebuiltTools))
                    {
                        output.Writer.TryWrite(item);
                    }
                    messageSucceeded = true;
                }
                finally
                {
                    if (prebuiltTools is not null)
                        await prebuiltTools.DisposeAsync();
                }
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
            await PostProcessAsync(previousBehaviorState, messageSucceeded, dispatch, ct);
        }
    }

    // ── Pet 编排方法（internal）──────────────────────────────────────────

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
    /// Used by MicroPet to build <see cref="PetOverrides"/>.
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

    // ── Agent/Tool 辅助方法 ──────────────────────────────────────────────────

    private AgentEntity? ResolveAgent(string? agentId) =>
        string.IsNullOrWhiteSpace(agentId)
            ? _agentStore.GetDefaultAgent()
            : _agentStore.GetAgentById(agentId) ?? _agentStore.GetDefaultAgent();

    private ToolCreationContext BuildToolContext(AgentEntity agent) => new(
        SessionId: MicroSession.Id,
        ChannelType: MicroSession.ChannelType,
        ChannelId: MicroSession.ChannelId,
        DisabledSkillIds: agent.DisabledSkillIds,
        CallingAgentId: agent.Id,
        AllowedSubAgentIds: agent.AllowedSubAgentIds);

    // ── 对话生命周期编排 ───────────────────────────────────────────────────────

    /// <summary>
    /// 按指定 <see cref="ChatLifecyclePhase"/> 依次回调已挂载的 <see cref="PetComponent"/>，按
    /// <see cref="PetComponent.Order"/> 升序串行执行。没有任何组件时零开销直接返回。
    /// <para>
    /// 异常不在此处吞掉：按调用点（当前为 <c>HandleChatAsync</c>/<c>HandleMessageAsync</c>）的
    /// 语义决定是否捕获。本轮尚未提供任何 <see cref="PetComponent"/> 子类，故所有 Phase 实际都是 no-op。
    /// </para>
    /// </summary>
    private async ValueTask RunPhaseAsync(ChatLifecyclePhase phase, ChatContext ctx)
    {
        PetComponent[] components = Components
            .OfType<PetComponent>()
            .OrderBy(c => c.Order)
            .ToArray();

        if (components.Length == 0)
            return;

        foreach (PetComponent component in components)
        {
            ValueTask invocation = phase switch
            {
                ChatLifecyclePhase.BeforeChat => component.OnBeforeChatAsync(ctx),
                ChatLifecyclePhase.AfterChat => component.OnAfterChatAsync(ctx),
                ChatLifecyclePhase.BeforeToolCall => component.OnBeforeToolCallAsync(ctx),
                ChatLifecyclePhase.AfterToolCall => component.OnAfterToolCallAsync(ctx),
                _ => ValueTask.CompletedTask,
            };
            await invocation;
        }
    }

    // ── MicroObject 生命周期回调 ───────────────────────────────────────────────

    /// <summary>
    /// 对象释放时把 Pet 标记为 <see cref="PetContextState.Disabled"/>，防止此后再被外部错误地当成活对象使用。
    /// 其余生命周期回调（Initialize / Activate / Deactivate / Uninitialize）沿用 <see cref="MicroClaw.Core.MicroObject"/>
    /// 的默认实现，会把已挂载的 <see cref="PetComponent"/> 同步推进/回退。
    /// </summary>
    protected override async ValueTask OnDisposedAsync(CancellationToken cancellationToken = default)
    {
        State = PetContextState.Disabled;
        await base.OnDisposedAsync(cancellationToken);
    }
}
