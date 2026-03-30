using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using MicroClaw.Agent.ContextProviders;
using MicroClaw.Agent.Dev;
using MicroClaw.Agent.Sessions;
using MicroClaw.Agent.Memory;
using MicroClaw.Agent.Middleware;
using MicroClaw.Agent.Streaming;
using MicroClaw.Agent.Streaming.Handlers;
using MicroClaw.Agent.Restorers;
using MicroClaw.Emotion;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Gateway.Contracts.Streaming;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers;
using MicroClaw.Safety;
using MicroClaw.Skills;
using MicroClaw.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 执行引擎：实现 ReAct 循环（推理 → 工具调用 → 观察 → 循环）。
/// System Prompt 由各 <see cref="IAgentContextProvider"/> 按 Order 顺序聚合构成。
/// MCP 工具从全局 McpServerConfigStore 加载，按 Agent.DisabledMcpServerIds 排除。
/// 实现 IAgentMessageHandler，供渠道消息处理器路由调用。
/// </summary>
public sealed class AgentRunner(
    AgentStore agentStore,
    IEnumerable<IAgentContextProvider> contextProviders,
    ProviderConfigStore providerStore,
    ProviderClientFactory clientFactory,
    ISessionReader sessionReader,
    SkillToolFactory skillToolFactory,
    IUsageTracker usageTracker,
    ILoggerFactory loggerFactory,
    IAgentStatusNotifier agentStatusNotifier,
    ToolCollector toolCollector,
    IDevMetricsService devMetrics,
    AIContentPipeline contentPipeline,
    IEnumerable<IChatContentRestorer> chatContentRestorers,
    IEmotionStore? emotionStore = null,
    IEmotionRuleEngine? emotionRuleEngine = null,
    IEmotionBehaviorMapper? emotionBehaviorMapper = null,
    IToolRiskRegistry? toolRiskRegistry = null,
    IToolRiskInterceptor? toolRiskInterceptor = null,
    IProviderRouter? providerRouter = null,
    IContextOverflowSummarizer? contextOverflowSummarizer = null) : IAgentMessageHandler
{
    private readonly ILogger<AgentRunner> _logger = loggerFactory.CreateLogger<AgentRunner>();
    private readonly IReadOnlyList<IAgentContextProvider> _contextProviders =
        contextProviders.OrderBy(p => p.Order).ToList().AsReadOnly();
    private readonly IReadOnlyList<IChatContentRestorer> _restorers =
        chatContentRestorers.ToList().AsReadOnly();
    private readonly IEmotionStore? _emotionStore = emotionStore;
    private readonly IEmotionRuleEngine? _emotionRuleEngine = emotionRuleEngine;
    private readonly IEmotionBehaviorMapper? _emotionBehaviorMapper = emotionBehaviorMapper;
    private readonly IToolRiskRegistry? _toolRiskRegistry = toolRiskRegistry;
    private readonly IToolRiskInterceptor? _toolRiskInterceptor = toolRiskInterceptor;
    private readonly IProviderRouter? _providerRouter = providerRouter;
    private readonly IContextOverflowSummarizer? _contextOverflowSummarizer = contextOverflowSummarizer;

    // ── IAgentMessageHandler ────────────────────────────────────────────────

    /// <summary>所有渠道消息默认路由到主 Agent（IsDefault=true），不再按渠道绑定匹配。</summary>
    public bool HasAgentForChannel(string channelId)
    {
        AgentConfig? main = agentStore.GetDefault();
        return main is { IsEnabled: true };
    }

    public async IAsyncEnumerable<StreamItem> HandleMessageAsync(
        string channelId,
        string sessionId,
        IReadOnlyList<SessionMessage> history,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        AgentConfig? agent = agentStore.GetDefault();
        if (agent is null || !agent.IsEnabled)
            throw new InvalidOperationException("No enabled default agent found.");

        // 从 session 获取 providerId；若 session 未绑定模型，按 Agent 路由策略选择
        SessionInfo? session = sessionReader.Get(sessionId);
        string providerId = !string.IsNullOrWhiteSpace(session?.ProviderId)
            ? session.ProviderId
            : ResolveProviderByStrategy(agent.RoutingStrategy);

        await foreach (StreamItem item in StreamReActAsync(agent, providerId, history, sessionId, ct, source: "channel"))
            yield return item;
    }

    // ── Provider 路由策略辅助 ───────────────────────────────────────────────

    /// <summary>
    /// 当 Session 未显式绑定 Provider 时，按 Agent 路由策略从已启用 Provider 中自动选择。
    /// 降级链：<see cref="IProviderRouter"/> → <see cref="ProviderConfigStore.GetDefault"/> → 空字符串。
    /// </summary>
    private string ResolveProviderByStrategy(ProviderRoutingStrategy strategy)
    {
        if (_providerRouter is not null)
        {
            ProviderConfig? routed = _providerRouter.Route(providerStore.All, strategy);
            if (routed is not null)
                return routed.Id;
        }
        return providerStore.GetDefault()?.Id ?? string.Empty;
    }

    // ── 流式 ReAct 循环（AF ChatClientAgent + FunctionInvokingChatClient + Channel 事件桥接）──

    public async IAsyncEnumerable<StreamItem> StreamReActAsync(
        AgentConfig agent,
        string providerId,
        IReadOnlyList<SessionMessage> history,
        string? sessionId = null,
        [EnumeratorCancellation] CancellationToken ct = default,
        string source = "chat")
    {
        // ── 薄迭代器：delegating to non-iterator ExecuteStreamingCoreAsync ──────────
        // C# 规则：yield return 不能置于 try-catch 块中，因此将含回退逻辑的非迭代器方法
        // 通过 Channel 与迭代器解耦，迭代器只负责从 Channel 读取并 yield。
        var outputChannel = System.Threading.Channels.Channel.CreateUnbounded<StreamItem>();
        Task execution = ExecuteStreamingCoreAsync(agent, providerId, history, sessionId, ct, source, outputChannel);

        try
        {
            await foreach (StreamItem item in outputChannel.Reader.ReadAllAsync(ct))
                yield return item;
        }
        finally
        {
            // 确保后台任务的任何异常被观察到（Channel 已 drain，不会重复 yield）
            try { await execution; }
            catch (OperationCanceledException) { /* 取消时静默 */ }
            catch { /* 异常已通过 Channel 传播给调用方，此处忽略重复抛出 */ }
        }
    }

    // ── 核心执行逻辑（非迭代器，可自由使用 try-catch）──────────────────────────

    /// <summary>
    /// 使用 Provider 回退链执行流式推理。失败且尚未产生任何输出时自动切换到下一个 Provider。
    /// 始终通过 <paramref name="output"/> Channel 完成（正常或带异常），供迭代器包装层读取。
    /// </summary>
    private async Task ExecuteStreamingCoreAsync(
        AgentConfig agent,
        string primaryProviderId,
        IReadOnlyList<SessionMessage> history,
        string? sessionId,
        CancellationToken ct,
        string source,
        System.Threading.Channels.Channel<StreamItem> output)
    {
        IReadOnlyList<ProviderConfig> chain = BuildFallbackChain(primaryProviderId, agent.RoutingStrategy);

        if (chain.Count == 0)
        {
            output.Writer.TryComplete(
                new InvalidOperationException($"Provider '{primaryProviderId}' not found or disabled."));
            return;
        }

        Exception? lastException = null;

        try
        {
        for (int attempt = 0; attempt < chain.Count; attempt++)
        {
            ProviderConfig provider = chain[attempt];
            bool isLastAttempt = attempt == chain.Count - 1;
            bool anyItemWritten = false;

            if (ct.IsCancellationRequested)
            {
                output.Writer.TryComplete();
                return;
            }

            if (attempt > 0)
            {
                _logger.LogWarning(
                    "Provider '{PrimaryId}' failed, falling back to '{FallbackId}' (attempt {Attempt}/{Total})",
                    chain[attempt - 1].Id, provider.Id, attempt + 1, chain.Count);
            }

            // 情绪状态（在 finally 中需要访问，故在 try 外声明）
            EmotionState emotionState = EmotionState.Default;
            BehaviorProfile? behaviorProfile = null;
            bool succeeded = false;
            Exception? streamingException = null;

            try
            {
                // ── 阶段 1：Setup ────────────────────────────────────────────
                IReadOnlyList<SessionMessage> validatedHistory = ValidateModalities(history, provider);
                SkillContext skillCtx = skillToolFactory.BuildSkillContext(agent.DisabledSkillIds, sessionId);

                // 情绪：执行前读取状态，映射行为模式
                if (_emotionStore is not null)
                    emotionState = await _emotionStore.GetCurrentAsync(agent.Id, ct);
                behaviorProfile = _emotionBehaviorMapper?.GetProfile(emotionState);

                List<ChatMessage> messages = await BuildChatMessagesAsync(
                    agent, validatedHistory, sessionId, skillCtx.CatalogFragment,
                    behaviorSuffix: behaviorProfile?.SystemPromptSuffix,
                    providerId: provider.Id, ct);

                // 工具收集
                SessionInfo? sessionForTools = !string.IsNullOrWhiteSpace(sessionId)
                    ? sessionReader.Get(sessionId) : null;

                var ancestorAgentIds = new List<string>();
                if (sessionForTools?.ParentSessionId is not null)
                {
                    string? cursor = sessionForTools.ParentSessionId;
                    while (cursor is not null)
                    {
                        SessionInfo? ancestor = sessionReader.Get(cursor);
                        if (ancestor is null) break;
                        if (!string.IsNullOrWhiteSpace(ancestor.AgentId))
                            ancestorAgentIds.Add(ancestor.AgentId);
                        cursor = ancestor.ParentSessionId;
                    }
                }

                var toolContext = new ToolCreationContext(
                    SessionId: sessionId,
                    ChannelType: sessionForTools?.ChannelType,
                    ChannelId: sessionForTools?.ChannelId,
                    DisabledSkillIds: agent.DisabledSkillIds,
                    CallingAgentId: agent.Id,
                    AllowedSubAgentIds: agent.AllowedSubAgentIds,
                    AncestorAgentIds: ancestorAgentIds.Count > 0 ? ancestorAgentIds : null);
                await using ToolCollectionResult toolResult =
                    await toolCollector.CollectToolsAsync(agent, toolContext, ct);

                _logger.LogInformation("Agent {AgentId} streaming with {ToolCount} tools via provider {ProviderId}",
                    agent.Id, toolResult.AllTools.Count, provider.Id);

                // AgentFactory：创建 ChatClientAgent + 事件 Channel
                ChatOptions chatOptions = BuildChatOptions(
                    toolResult.AllTools, provider, skillCtx.ModelOverride, skillCtx.EffortOverride,
                    temperatureOverride: behaviorProfile?.Temperature,
                    topPOverride: behaviorProfile?.TopP);
                IChatClient rawClient = clientFactory.Create(provider);
                var (chatAgent, eventChannel, runOptions, tracker) = AgentFactory.Create(
                    rawClient, agent.Name, chatOptions, loggerFactory,
                    devMetrics: devMetrics,
                    riskRegistry: _toolRiskRegistry,
                    riskInterceptor: _toolRiskInterceptor);

                if (!string.IsNullOrWhiteSpace(sessionId))
                    await agentStatusNotifier.NotifyAsync(sessionId, agent.Id, "running", ct);

                // AF AgentSession：将 MicroClaw Session 元数据注入 StateBag
                AgentSession afSession = await chatAgent.CreateSessionAsync(ct);
                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                    SessionInfo? sessionInfo = sessionReader.Get(sessionId);
                    if (sessionInfo is not null)
                        AgentSessionAdapter.PopulateStateBag(afSession.StateBag, sessionInfo);
                }

                UsageCapture usageCapture = new();
                Task? runTask = null;
                var runSw = System.Diagnostics.Stopwatch.StartNew();
                UsageContentHandler.BindCapture(usageCapture);

                // ── 阶段 2：Streaming（内层 try-finally 负责清理）────────────
                try
                {
                    // 后台任务：运行 AF agent，通过 AIContentPipeline 将内容写入内部 eventChannel
                    runTask = Task.Run(async () =>
                    {
                        try
                        {
                            await foreach (AgentResponseUpdate update in chatAgent.RunStreamingAsync(
                                               messages, session: afSession, runOptions, ct))
                            {
                                if (!string.IsNullOrEmpty(update.MessageId))
                                    tracker.Current = update.MessageId;

                                foreach (StreamItem item in contentPipeline.Process(update.Contents))
                                {
                                    item.MessageId ??= tracker.Current;
                                    await eventChannel.Writer.WriteAsync(item, ct);
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            // SSE 客户端断开或外部取消——静默结束
                        }
                        finally
                        {
                            eventChannel.Writer.TryComplete();
                        }
                    }, CancellationToken.None);

                    // 主流：读取内部 eventChannel，转发到外层 output channel
                    await foreach (StreamItem item in eventChannel.Reader.ReadAllAsync(ct))
                    {
                        anyItemWritten = true;
                        await output.Writer.WriteAsync(item, ct);
                    }

                    await runTask; // 等待后台任务；异常在此抛出
                    succeeded = true;
                }
                catch (OperationCanceledException)
                {
                    throw; // 取消直接向上传播，由外层的 OperationCanceledException catch 处理
                }
                catch (Exception ex) when (!anyItemWritten && !isLastAttempt)
                {
                    // 流式执行失败，且尚未向输出写入任何内容 → 可安全回退
                    streamingException = ex;
                }
                finally
                {
                    runSw.Stop();
                    devMetrics.RecordAgentRun(agent.Id, succeeded, runSw.ElapsedMilliseconds);
                    UsageContentHandler.UnbindCapture();
                    await UsageTrackingMiddleware.TrackAsync(
                        usageCapture, sessionId, provider, source,
                        usageTracker, _logger,
                        agentId: agent.Id,
                        monthlyBudgetUsd: agent.MonthlyBudgetUsd,
                        ct: CancellationToken.None);
                    if (!string.IsNullOrWhiteSpace(sessionId))
                        await agentStatusNotifier.NotifyAsync(
                            sessionId, agent.Id, succeeded ? "completed" : "failed", CancellationToken.None);

                    // 情绪：执行后更新情绪状态
                    if (_emotionStore is not null && _emotionRuleEngine is not null)
                    {
                        EmotionEventType emotionEvent = succeeded
                            ? EmotionEventType.TaskCompleted
                            : EmotionEventType.TaskFailed;
                        EmotionState newState = _emotionRuleEngine.Evaluate(emotionState, emotionEvent);
                        await _emotionStore.SaveAsync(agent.Id, newState, CancellationToken.None);
                        _logger.LogDebug("情绪更新：AgentId={AgentId} 事件={Event} 模式={Mode}",
                            agent.Id, emotionEvent, behaviorProfile?.Mode);
                    }
                }

                // streamingException 被内层 catch 捕获 → 尝试下一个 Provider
                if (streamingException is not null)
                {
                    lastException = streamingException;
                    _logger.LogWarning(streamingException,
                        "Provider '{ProviderId}' streaming failed without output (attempt {Attempt}/{Total}), will try fallback",
                        provider.Id, attempt + 1, chain.Count);
                    continue;
                }

                // 成功！完成输出 Channel
                output.Writer.TryComplete();
                return;
            }
            catch (OperationCanceledException)
            {
                output.Writer.TryComplete();
                return;
            }
            catch (Exception ex) when (!anyItemWritten && !isLastAttempt)
            {
                // Setup 阶段失败（Provider 尚未产生输出）→ 尝试下一个
                lastException = ex;
                _logger.LogWarning(ex,
                    "Provider '{ProviderId}' setup failed (attempt {Attempt}/{Total}), will try fallback",
                    provider.Id, attempt + 1, chain.Count);
                // 继续循环
            }
        }

        // 所有 Provider 均已耗尽
        output.Writer.TryComplete(
            lastException ?? new InvalidOperationException("All providers in fallback chain failed."));

        } // end try
        catch (Exception ex)
        {
            // 安全网：确保 output Channel 在任何未预料的异常路径下都被正确关闭，
            // 避免消费端 ReadAllAsync() 无限挂起。
            _logger.LogError(ex, "ExecuteStreamingCoreAsync 发生未处理异常，关闭 output Channel");
            output.Writer.TryComplete(ex);
        }
    }

    // ── Provider 回退链构建 ────────────────────────────────────────────────

    /// <summary>
    /// 构建 Provider 回退链：将指定 <paramref name="primaryProviderId"/> 排在链首，
    /// 其余按路由策略排序的已启用 Provider 依次跟随。
    /// 若未注册 <see cref="IProviderRouter"/>，则仅返回主 Provider（无回退）。
    /// </summary>
    private IReadOnlyList<ProviderConfig> BuildFallbackChain(
        string primaryProviderId,
        ProviderRoutingStrategy strategy)
    {
        IReadOnlyList<ProviderConfig> allProviders = providerStore.All;

        if (_providerRouter is null)
        {
            // 无路由器：仅使用主 Provider，不提供回退
            ProviderConfig? primary = allProviders.FirstOrDefault(
                p => p.Id == primaryProviderId && p.IsEnabled);
            return primary is not null ? [primary] : [];
        }

        IReadOnlyList<ProviderConfig> orderedChain = _providerRouter.GetFallbackChain(allProviders, strategy);

        // 将 primaryProviderId 移至链首（优先使用 Session/Agent 指定的 Provider）
        ProviderConfig? primaryInChain = orderedChain.FirstOrDefault(p => p.Id == primaryProviderId);
        if (primaryInChain is null || string.IsNullOrWhiteSpace(primaryProviderId))
        {
            // 主 Provider 未找到或未指定，直接使用策略顺序
            return orderedChain;
        }

        var result = new List<ProviderConfig>(orderedChain.Count) { primaryInChain };
        foreach (ProviderConfig p in orderedChain)
        {
            if (p.Id != primaryProviderId)
                result.Add(p);
        }
        return result.AsReadOnly();
    }

    /// <summary>
    /// 调用指定 Agent 的工具（MCP 或内置），返回工具输出字符串。
    /// 通过 ToolCollector 统一收集后按名称查找。供工作流 Tool 节点使用。
    /// </summary>
    public async Task<string> InvokeToolAsync(
        string agentId, string toolName, IReadOnlyDictionary<string, string>? nodeConfig, string fallbackInput, CancellationToken ct)
    {
        AgentConfig? agent = agentStore.GetById(agentId);
        if (agent is null || !agent.IsEnabled)
        {
            _logger.LogWarning("InvokeToolAsync: Agent '{AgentId}' not found or disabled.", agentId);
            return fallbackInput;
        }

        // 构建工具参数
        var arguments = new Dictionary<string, object?>();
        if (nodeConfig is not null)
        {
            foreach (var kv in nodeConfig)
            {
                if (kv.Key != "toolAgentId")
                    arguments[kv.Key] = kv.Value;
            }
        }
        if (arguments.Count == 0)
            arguments["input"] = fallbackInput;

        // 通过 ToolCollector 统一收集并查找工具
        var context = new ToolCreationContext();
        await using ToolCollectionResult toolResult = await toolCollector.CollectToolsAsync(agent, context, ct);

        AITool? tool = toolResult.AllTools.FirstOrDefault(t => t.Name == toolName)
            ?? toolResult.AllTools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));

        if (tool is AIFunction fn)
        {
            object? result = await fn.InvokeAsync(new AIFunctionArguments(arguments), ct);
            return result?.ToString() ?? string.Empty;
        }

        _logger.LogWarning(
            "InvokeToolAsync: Tool '{ToolName}' not found for Agent '{AgentId}'.",
            toolName, agentId);
        return fallbackInput;
    }

    // ── 私有辅助方法 ────────────────────────────────────────────────────────

    private async Task<List<ChatMessage>> BuildChatMessagesAsync(AgentConfig agent, IReadOnlyList<SessionMessage> history, string? sessionId = null, string? skillCatalogFragment = null, string? behaviorSuffix = null, string? providerId = null, CancellationToken ct = default)
    {
        // 提取最后一条用户消息，供 IUserAwareContextProvider（如 RagContextProvider）进行语义检索
        string? latestUserMessage = history
            .LastOrDefault(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase))
            ?.Content;

        string systemPrompt = await BuildSystemPromptAsync(agent, sessionId, skillCatalogFragment, latestUserMessage, behaviorSuffix, ct);
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            int promptBytes = System.Text.Encoding.UTF8.GetByteCount(systemPrompt);
            _logger.LogDebug("System Prompt 注入：{Bytes} 字节（DNA+记忆），Session={SessionId}", promptBytes, sessionId);
        }

        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));

        // 应用滑动窗口：若配置了 ContextWindowMessages，只取最近 N 条消息传给 LLM
        IEnumerable<SessionMessage> windowed;
        if (agent.ContextWindowMessages.HasValue && history.Count > agent.ContextWindowMessages.Value)
        {
            windowed = history.TakeLast(agent.ContextWindowMessages.Value);

            // 触发异步溢出总结（fire-and-forget）
            if (_contextOverflowSummarizer is not null && !string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(providerId))
            {
                var overflowMessages = history.Take(history.Count - agent.ContextWindowMessages.Value).ToList();
                _ = _contextOverflowSummarizer.SummarizeAsync(sessionId, providerId, overflowMessages, CancellationToken.None);
            }
        }
        else
        {
            windowed = history;
        }

        // 过滤不可见消息，按 Id 分组，保持插入顺序
        var groups = new List<(string Id, List<SessionMessage> Items)>();
        string? currentGroupId = null;
        List<SessionMessage>? currentGroup = null;

        foreach (SessionMessage msg in windowed)
        {
            if (!MessageVisibility.IsVisibleToLlm(msg.Visibility))
                continue;

            if (msg.Id != currentGroupId)
            {
                currentGroupId = msg.Id;
                currentGroup = [msg];
                groups.Add((msg.Id, currentGroup));
            }
            else
            {
                currentGroup!.Add(msg);
            }
        }

        // 每组内按 Role 拆分为 ChatMessage：先 assistant（含 thinking + text + tool_call + data），再 tool
        foreach (var (groupId, items) in groups)
        {
            // user 消息直接还原
            if (items.Count == 1 && items[0].Role == "user")
            {
                var contents = RestoreContents(items[0]);
                var chatMsg = new ChatMessage(ChatRole.User, contents) { MessageId = groupId };
                messages.Add(chatMsg);
                continue;
            }

            var assistantContents = new List<AIContent>();
            var toolContents = new List<AIContent>();

            foreach (SessionMessage msg in items)
            {
                if (msg.Role == "tool" || msg.MessageType == "tool_result")
                {
                    toolContents.AddRange(RestoreContents(msg));
                }
                else if (msg.Role is "system" && msg.MessageType is "sub_agent_start" or "sub_agent_result")
                {
                    // 子 Agent 消息跳过（不传给 LLM）
                    continue;
                }
                else
                {
                    assistantContents.AddRange(RestoreContents(msg));
                }
            }

            if (assistantContents.Count > 0)
            {
                ChatRole role = items[0].Role == "user" ? ChatRole.User : ChatRole.Assistant;
                messages.Add(new ChatMessage(role, assistantContents) { MessageId = groupId });
            }

            if (toolContents.Count > 0)
                messages.Add(new ChatMessage(ChatRole.Tool, toolContents) { MessageId = groupId });
        }

        return messages;
    }

    /// <summary>通过注册的 Restorer 将 SessionMessage 还原为 AIContent 列表。</summary>
    private List<AIContent> RestoreContents(SessionMessage msg)
    {
        var contents = new List<AIContent>();
        foreach (IChatContentRestorer restorer in _restorers)
        {
            if (restorer.CanRestore(msg))
                contents.AddRange(restorer.Restore(msg));
        }

        // 如果没有任何 Restorer 匹配但有内容，回退为 TextContent
        if (contents.Count == 0 && !string.IsNullOrEmpty(msg.Content))
            contents.Add(new TextContent(msg.Content));

        return contents;
    }

    /// <summary>
    /// 构建 System Prompt：按 <see cref="IAgentContextProvider.Order"/> 顺序聚合所有 Provider 的上下文片段，
    /// 末尾追加 Skill Context。
    /// </summary>
    /// <param name="agent">当前执行的 Agent 配置。</param>
    /// <param name="sessionId">当前会话 ID。</param>
    /// <param name="skillContext">技能目录片段（始终排最后）。</param>
    /// <param name="userMessage">当前用户消息文本，供 <see cref="IUserAwareContextProvider"/> 进行语义检索。</param>
    /// <param name="ct">取消令牌。</param>
    internal async ValueTask<string> BuildSystemPromptAsync(
        AgentConfig agent,
        string? sessionId,
        string? skillContext = null,
        string? userMessage = null,
        string? behaviorSuffix = null,
        CancellationToken ct = default)
    {
        var parts = new List<string>(_contextProviders.Count + 2);

        foreach (IAgentContextProvider provider in _contextProviders)
        {
            string? fragment = provider is IUserAwareContextProvider userAware
                ? await userAware.BuildContextAsync(agent, sessionId, userMessage, ct)
                : await provider.BuildContextAsync(agent, sessionId, ct);

            if (!string.IsNullOrWhiteSpace(fragment))
                parts.Add(fragment);
        }

        // Skill Context 始终排在最后（行为后缀除外）
        if (!string.IsNullOrWhiteSpace(skillContext))
            parts.Add(skillContext);

        // 行为模式后缀（由情绪系统注入）— 排在最末尾
        if (!string.IsNullOrWhiteSpace(behaviorSuffix))
            parts.Add(behaviorSuffix);

        return string.Join("\n\n", parts);
    }

    /// <summary>构建不带 UseFunctionInvocation 的客户端（供兼容方法使用，实际已被 AgentFactory 替代）。</summary>
    private IChatClient BuildRawClient(ProviderConfig provider) => clientFactory.Create(provider);

    /// <summary>校验消息历史中的附件是否被 Provider 模态能力支持。</summary>
    private IReadOnlyList<SessionMessage> ValidateModalities(
        IReadOnlyList<SessionMessage> history,
        ProviderConfig provider)
    {
        var caps = provider.Capabilities;
        // 快速路径：无附件则直接返回
        if (!history.Any(m => m.Attachments is { Count: > 0 }))
            return history;

        var filtered = new List<SessionMessage>(history.Count);
        foreach (SessionMessage msg in history)
        {
            if (msg.Attachments is not { Count: > 0 })
            {
                filtered.Add(msg);
                continue;
            }

            var kept = new List<MessageAttachment>();
            foreach (MessageAttachment att in msg.Attachments)
            {
                bool supported = att.MimeType switch
                {
                    string m when m.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => caps.InputImage,
                    string m when m.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) => caps.InputAudio,
                    string m when m.StartsWith("video/", StringComparison.OrdinalIgnoreCase) => caps.InputVideo,
                    _ => caps.InputFile, // 其他类型视为文件
                };

                if (supported)
                {
                    kept.Add(att);
                }
                else
                {
                    _logger.LogWarning(
                        "Attachment '{FileName}' ({MimeType}) skipped: provider '{Provider}' does not support this modality",
                        att.FileName, att.MimeType, provider.DisplayName);
                }
            }

            // 重建消息：保留受支持的附件，其余丢弃
            filtered.Add(msg with { Attachments = kept.Count > 0 ? kept : null });
        }

        return filtered;
    }

    private static ChatOptions BuildChatOptions(
        IReadOnlyList<AITool> tools,
        ProviderConfig provider,
        string? modelOverride = null,
        string? effortOverride = null,
        float? temperatureOverride = null,
        float? topPOverride = null)
    {
        var options = new ChatOptions
        {
            ModelId = modelOverride ?? provider.ModelName,
            MaxOutputTokens = provider.MaxOutputTokens,
            Temperature = temperatureOverride,
            TopP = topPOverride,
        };
        if (!string.IsNullOrWhiteSpace(effortOverride))
            options.AdditionalProperties ??= new() { ["thinking_effort"] = effortOverride };
        // 仅在 Provider 声明支持 Function Calling 时附加工具
        if (tools.Count > 0 && provider.Capabilities.SupportsFunctionCalling)
            options.Tools = [.. tools];
        options.ToolMode = ChatToolMode.Auto;
        options.AllowMultipleToolCalls = true;
        return options;
    }
}
