using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MicroClaw.Agent;
using AgentEntity = MicroClaw.Agent.Agent;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Pet.Decision;
using MicroClaw.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Pet;

/// <summary>
/// Pet 会话编排引擎（薄调度层）：获取 session → IPet → 工具组装 → AgentRunner 执行。
/// <para>
/// 所有决策/后处理/情绪/RAG 逻辑均由 <see cref="MicroPet"/> 内部子系统完成，
/// PetRunner 仅负责编排流程和工具组装。
/// </para>
/// <para>
/// 实现 <see cref="IAgentMessageHandler"/> 供渠道消息处理器路由调用。
/// </para>
/// </summary>
public sealed class PetRunner : IPetRunner, IAgentMessageHandler, IService
{
    private readonly AgentRunner _agentRunner;
    private readonly AgentStore _agentStore;
    private readonly ISessionService _sessionRepo;
    private readonly PetContextFactory _petContextFactory;
    private readonly ToolCollector _toolCollector;
    private readonly ILogger<PetRunner> _logger;

    public PetRunner(IServiceProvider sp)
    {
        _agentRunner = sp.GetRequiredService<AgentRunner>();
        _agentStore = sp.GetRequiredService<AgentStore>();
        _sessionRepo = sp.GetRequiredService<ISessionService>();
        _petContextFactory = sp.GetRequiredService<PetContextFactory>();
        _toolCollector = sp.GetRequiredService<ToolCollector>();
        _logger = sp.GetRequiredService<ILogger<PetRunner>>();
    }

    // ── IService ─────────────────────────────────────────────────────────
    public int InitOrder => 25;
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

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

        // ── 1. 获取 Session ──
        IMicroSession? session = _sessionRepo.Get(sessionId);

        // ── 2. 获取 Pet（懒加载：服务重启后 Pet 为 null）──
        MicroPet? petCtx = session?.Pet as MicroPet;
        if (petCtx is null && session is { IsApproved: true })
        {
            MicroPet? loaded = await _petContextFactory.LoadAsync(session, ct);
            if (loaded is not null)
                petCtx = loaded;
        }

        // ── 3. Pet 未启用：ToolCollector → AgentRunner（prebuiltTools）──
        if (petCtx is null || !petCtx.IsEnabled)
        {
            _logger.LogDebug("Pet 未启用 (SessionId={SessionId})，透传 AgentRunner", sessionId);

            AgentEntity? agent = ResolveAgent(session?.AgentId);
            if (agent is null || !agent.IsEnabled)
                throw new InvalidOperationException("No enabled agent found for this session.");

            string providerId = session?.ProviderId ?? string.Empty;

            var toolContext = new ToolCreationContext(
                SessionId: sessionId,
                ChannelType: session?.ChannelType,
                ChannelId: session?.ChannelId,
                DisabledSkillIds: agent.DisabledSkillIds,
                CallingAgentId: agent.Id,
                AllowedSubAgentIds: agent.AllowedSubAgentIds);
            ToolCollectionResult? prebuiltTools = null;
            try
            {
                prebuiltTools = await _toolCollector.CollectToolsAsync(agent, toolContext, ct);
                await foreach (var item in _agentRunner.StreamReActAsync(
                    agent, providerId, history, sessionId, ct, source, prebuiltTools: prebuiltTools))
                {
                    yield return item;
                }
            }
            finally
            {
                if (prebuiltTools is not null)
                    await prebuiltTools.DisposeAsync();
            }
            yield break;
        }

        // ── 4. Pet 启用：Channel 解耦迭代器 ──
        var outputChannel = Channel.CreateUnbounded<StreamItem>();
        Task execution = ExecuteWithPetAsync(sessionId, session, history, petCtx, ct, source, outputChannel);

        await foreach (var item in outputChannel.Reader.ReadAllAsync(ct))
            yield return item;

        try { await execution; }
        catch (OperationCanceledException) { /* 取消时静默 */ }
    }

    /// <summary>
    /// Pet 编排核心逻辑（非迭代器，可自由使用 try-catch）。
    /// </summary>
    private async Task ExecuteWithPetAsync(
        string sessionId,
        IMicroSession? session,
        IReadOnlyList<SessionMessage> history,
        MicroPet microPetCtx,
        CancellationToken ct,
        string source,
        Channel<StreamItem> output)
    {
        PetDispatchResult dispatch = new() { Reason = "初始化" };
        bool messageSucceeded = false;
        PetBehaviorState previousBehaviorState = microPetCtx.PetState.BehaviorState;

        try
        {
            // ── Enter Dispatching state ──
            previousBehaviorState = await microPetCtx.EnterDispatchingAsync(ct);

            // ── Pet LLM 决策 ──
            dispatch = await microPetCtx.DecideAsync(history, previousBehaviorState, ct);

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
                // 解析 Agent / Provider
                AgentEntity? agent = !string.IsNullOrWhiteSpace(dispatch.AgentId)
                    ? _agentStore.GetAgentById(dispatch.AgentId) ?? ResolveAgent(session?.AgentId)
                    : ResolveAgent(session?.AgentId);
                if (agent is null || !agent.IsEnabled)
                    throw new InvalidOperationException("No enabled agent found for this session.");

                string providerId = !string.IsNullOrWhiteSpace(dispatch.ProviderId)
                    ? dispatch.ProviderId
                    : session?.ProviderId ?? string.Empty;

                // 收集工具：ToolCollector 通用工具 + Channel 工具
                var toolContext = new ToolCreationContext(
                    SessionId: sessionId,
                    ChannelType: session?.ChannelType,
                    ChannelId: session?.ChannelId,
                    DisabledSkillIds: agent.DisabledSkillIds,
                    CallingAgentId: agent.Id,
                    AllowedSubAgentIds: agent.AllowedSubAgentIds);

                ToolCollectionResult? prebuiltTools = null;
                try
                {
                    prebuiltTools = await _toolCollector.CollectToolsAsync(agent, toolContext, ct);

                    // Merge channel tools from Pet
                    IReadOnlyList<AIFunction> channelTools = await microPetCtx.CollectChannelToolsAsync(ct);
                    if (channelTools.Count > 0)
                        prebuiltTools.AddTools(channelTools);

                    // 构建 PetOverrides
                    var behaviorProfile = microPetCtx.GetBehaviorProfile();
                    var petOverrides = new PetOverrides
                    {
                        Temperature = behaviorProfile.Temperature,
                        TopP = behaviorProfile.TopP,
                        BehaviorSuffix = behaviorProfile.SystemPromptSuffix,
                        ToolOverrides = dispatch.ToolOverrides is { Count: > 0 } ? dispatch.ToolOverrides : null,
                        PetKnowledge = dispatch.PetKnowledge,
                    };

                    await foreach (var item in _agentRunner.StreamReActAsync(
                        agent, providerId, history, sessionId, ct, source, petOverrides,
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
            // ── 后处理：委托 PetContext ──
            await microPetCtx.PostProcessAsync(previousBehaviorState, messageSucceeded, dispatch, ct);
        }
    }

    // ── IAgentMessageHandler ────────────────────────────────────────────────

    /// <summary>所有渠道消息默认路由到默认 Agent（IsDefault=true），不再按渠道绑定匹配。</summary>
    public bool HasAgentForChannel(string channelId)
    {
        AgentEntity? main = _agentStore.GetDefaultAgent();
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

    // ── 内部辅助 ────────────────────────────────────────────────────────────

    private AgentEntity? ResolveAgent(string? agentId) =>
        string.IsNullOrWhiteSpace(agentId)
            ? _agentStore.GetDefaultAgent()
            : _agentStore.GetAgentById(agentId) ?? _agentStore.GetDefaultAgent();
}

