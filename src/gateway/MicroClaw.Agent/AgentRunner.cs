using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using MicroClaw.Agent.ContextProviders;
using MicroClaw.Agent.Dev;
using MicroClaw.Agent.Sessions;
using MicroClaw.Agent.Memory;
using MicroClaw.Agent.Restorers;
using MicroClaw.RAG;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Infrastructure;
using MicroClaw.Plugins.Hooks;
using MicroClaw.Providers;
using MicroClaw.Skills;
using MicroClaw.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 执行引擎：实现 ReAct 循环（推理 → 工具调用 → 观察 → 循环）。
/// System Prompt 由各 <see cref="IAgentContextProvider"/> 按 Order 顺序聚合构成。
/// MCP 工具从全局 McpServerConfigStore 加载，按 Agent.DisabledMcpServerIds 排除。
/// 实现 IAgentMessageHandler，供渠道消息处理器路由调用。
/// </summary>
public sealed class AgentRunner : IAgentMessageHandler, IService
{
    private readonly AgentStore _agentStore;
    private readonly ILogger<AgentRunner> _logger;
    private readonly IReadOnlyList<IAgentContextProvider> _contextProviders;
    private readonly ProviderService _providerService;
    private readonly ISessionService _sessionReader;
    private readonly SkillToolFactory _skillToolFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IAgentStatusNotifier _agentStatusNotifier;
    private readonly ToolCollector _toolCollector;
    private readonly IDevMetricsService _devMetrics;
    private readonly ChatContentRestorerService _restorerService;
    private readonly IProviderRouter? _providerRouter;
    private readonly IContextOverflowSummarizer? _contextOverflowSummarizer;
    private readonly IHookExecutor? _hookExecutor;
    private readonly IRagUsageAuditor? _ragUsageAuditor;
    private readonly RagRetrievalContext? _ragRetrievalContext;

    public AgentRunner(IServiceProvider sp)
    {
        _agentStore = sp.GetRequiredService<AgentStore>();
        _loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        _logger = _loggerFactory.CreateLogger<AgentRunner>();
        _contextProviders = sp.GetServices<IAgentContextProvider>().OrderBy(p => p.Order).ToList().AsReadOnly();
        _providerService = sp.GetRequiredService<ProviderService>();
        _sessionReader = sp.GetRequiredService<ISessionService>();
        _skillToolFactory = sp.GetRequiredService<SkillToolFactory>();
        _agentStatusNotifier = sp.GetRequiredService<IAgentStatusNotifier>();
        _toolCollector = sp.GetRequiredService<ToolCollector>();
        _devMetrics = sp.GetRequiredService<IDevMetricsService>();
        _restorerService = sp.GetRequiredService<ChatContentRestorerService>();
        _providerRouter = sp.GetService<IProviderRouter>();
        _contextOverflowSummarizer = sp.GetService<IContextOverflowSummarizer>();
        _hookExecutor = sp.GetService<IHookExecutor>();
        _ragUsageAuditor = sp.GetService<IRagUsageAuditor>();
        _ragRetrievalContext = sp.GetService<RagRetrievalContext>();
    }

    // ── IService ─────────────────────────────────────────────────────────
    public int InitOrder => 20;
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ── IAgentMessageHandler ────────────────────────────────────────────────

    /// <summary>所有渠道消息默认路由到主 Agent（IsDefault=true），不再按渠道绑定匹配。</summary>
    public bool HasAgentForChannel(string channelId)
    {
        Agent? main = _agentStore.GetDefaultAgent();
        return main is { IsEnabled: true };
    }

    public async IAsyncEnumerable<StreamItem> HandleMessageAsync(
        string channelId,
        string sessionId,
        IReadOnlyList<SessionMessage> history,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        Agent? agent = _agentStore.GetDefaultAgent();
        if (agent is null || !agent.IsEnabled)
            throw new InvalidOperationException("No enabled default agent found.");

        // 从 session 获取 providerId；若 session 未绑定模型，按 Agent 路由策略选择
        IMicroSession? session = _sessionReader.Get(sessionId);
        string providerId = !string.IsNullOrWhiteSpace(session?.ProviderId)
            ? session.ProviderId
            : ResolveProviderByStrategy(agent.RoutingStrategy);

        await foreach (StreamItem item in StreamReActAsync(agent, providerId, history, sessionId, ct, source: "channel"))
            yield return item;
    }

    // ── Provider 路由策略辅助 ───────────────────────────────────────────────

    /// <summary>
    /// 当 Session 未显式绑定 Provider 时，按 Agent 路由策略从已启用 Provider 中自动选择。
    /// 降级链：<see cref="IProviderRouter"/> → <see cref="ProviderService.GetDefault"/> → 空字符串。
    /// </summary>
    private string ResolveProviderByStrategy(ProviderRoutingStrategy strategy)
    {
        if (_providerRouter is not null)
        {
            ProviderConfig? routed = _providerRouter.Route(_providerService.All, strategy);
            if (routed is not null)
                return routed.Id;
        }
        return _providerService.GetDefault()?.Id ?? string.Empty;
    }

    // ── 流式 ReAct 循环（AF ChatClientAgent + FunctionInvokingChatClient + Channel 事件桥接）──

    public async IAsyncEnumerable<StreamItem> StreamReActAsync(
        Agent agent,
        string providerId,
        IReadOnlyList<SessionMessage> history,
        string? sessionId = null,
        [EnumeratorCancellation] CancellationToken ct = default,
        string source = "chat",
        PetOverrides? petOverrides = null,
        IReadOnlyList<string>? ancestorAgentIdsOverride = null,
        ToolCollectionResult? prebuiltTools = null)
    {
        // ── 薄迭代器：delegating to non-iterator ExecuteStreamingCoreAsync ──────────
        // C# 规则：yield return 不能置于 try-catch 块中，因此将含回退逻辑的非迭代器方法
        // 通过 Channel 与迭代器解耦，迭代器只负责从 Channel 读取并 yield。
        var outputChannel = System.Threading.Channels.Channel.CreateUnbounded<StreamItem>();
        Task execution = ExecuteStreamingCoreAsync(
            agent, providerId, history, sessionId, ct, source, outputChannel, petOverrides, ancestorAgentIdsOverride,
            prebuiltTools);

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
        Agent agent,
        string primaryProviderId,
        IReadOnlyList<SessionMessage> history,
        string? sessionId,
        CancellationToken ct,
        string source,
        System.Threading.Channels.Channel<StreamItem> output,
        PetOverrides? petOverrides = null,
        IReadOnlyList<string>? ancestorAgentIdsOverride = null,
        ToolCollectionResult? prebuiltTools = null)
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

            // 执行状态标志
            bool succeeded = false;
            Exception? streamingException = null;

            try
            {
                // ── 阶段 1：Setup ────────────────────────────────────────────
                IReadOnlyList<SessionMessage> validatedHistory = ValidateModalities(history, provider);
                SkillContext skillCtx = _skillToolFactory.BuildSkillContext(agent.DisabledSkillIds, sessionId);

                List<ChatMessage> messages = await BuildChatMessagesAsync(
                    agent, validatedHistory, sessionId, skillCtx.CatalogFragment,
                    behaviorSuffix: petOverrides?.BehaviorSuffix,
                    providerId: provider.Id, ct);

                // Pet 知识注入：作为独立的 system 消息追加到消息列表开头之后
                if (!string.IsNullOrWhiteSpace(petOverrides?.PetKnowledge))
                {
                    // 在第一条 system 消息之后插入 Pet 知识
                    int insertIdx = messages.Count > 0 && messages[0].Role == ChatRole.System ? 1 : 0;
                    messages.Insert(insertIdx, new ChatMessage(ChatRole.System,
                        $"[Pet 背景知识]\n{petOverrides.PetKnowledge}"));
                }

                // 工具收集（prebuiltTools != null 时由调用方管理释放，跳过内部收集）
                ToolCollectionResult toolResult;
                bool ownsToolResult;
                if (prebuiltTools is not null)
                {
                    toolResult = prebuiltTools;
                    ownsToolResult = false;
                }
                else
                {
                    Agent effectiveAgent = petOverrides?.ToolOverrides is { Count: > 0 }
                        ? agent.WithToolOverrides(petOverrides.ToolOverrides)
                        : agent;
                    IMicroSession? sessionForTools = !string.IsNullOrWhiteSpace(sessionId)
                        ? _sessionReader.Get(sessionId) : null;

                    var ancestorAgentIds = new List<string>();
                    if (ancestorAgentIdsOverride is not null)
                    {
                        ancestorAgentIds.AddRange(
                            ancestorAgentIdsOverride.Where(static id => !string.IsNullOrWhiteSpace(id)));
                    }
                    else if (SubAgentRunScope.Current?.AgentChain is { Count: > 0 } currentAgentChain)
                    {
                        ancestorAgentIds.AddRange(currentAgentChain);
                    }

                    var toolContext = new ToolCreationContext(
                        SessionId: sessionId,
                        ChannelType: sessionForTools?.ChannelType,
                        ChannelId: sessionForTools?.ChannelId,
                        DisabledSkillIds: agent.DisabledSkillIds,
                        CallingAgentId: agent.Id,
                        AllowedSubAgentIds: agent.AllowedSubAgentIds,
                        AncestorAgentIds: ancestorAgentIds.Count > 0 ? ancestorAgentIds : null);
                    toolResult = await _toolCollector.CollectToolsAsync(effectiveAgent, toolContext, ct);
                    ownsToolResult = true;
                }

                _logger.LogInformation("Agent {AgentId} streaming with {ToolCount} tools via provider {ProviderId}",
                    agent.Id, toolResult.AllTools.Count, provider.Id);

                ChatMicroProvider chatProvider = _providerService.TryGetProvider(provider.Id)
                    ?? throw new InvalidOperationException(
                        $"Chat provider '{provider.Id}' is not available in cache.");

                ChatOptions chatOptions = BuildChatOptions(
                    toolResult.AllTools, provider, skillCtx.ModelOverride, skillCtx.EffortOverride,
                    temperatureOverride: petOverrides?.Temperature,
                    topPOverride: petOverrides?.TopP);

                // MicroChatContext：Provider 内部依此归属 usage。
                // TODO: 待 Pet 管线接入后，通过 MicroChatContext 传递 History/Pet/Channel/Output 等。
                IMicroSession? sessionForCtx = !string.IsNullOrWhiteSpace(sessionId)
                    ? _sessionReader.Get(sessionId) : null;
                MicroChatContext chatCtx = sessionForCtx is not null
                    ? MicroChatContext.ForSystem(sessionForCtx, source, ct)
                    : MicroChatContext.ForSystem(
                        !string.IsNullOrWhiteSpace(sessionId) ? sessionId : $"agent:{agent.Id}",
                        source,
                        ct);

                if (!string.IsNullOrWhiteSpace(sessionId))
                    await _agentStatusNotifier.NotifyAsync(sessionId, agent.Id, "running", ct);

                // 插件 Hook：SessionStart
                if (_hookExecutor is not null)
                {
                    _ = _hookExecutor.ExecuteAsync(new HookContext
                    {
                        Event = HookEvent.SessionStart,
                        SessionId = sessionId,
                        AgentId = agent.Id
                    }, CancellationToken.None);
                }

                var runSw = System.Diagnostics.Stopwatch.StartNew();

                // ── 阶段 2：Streaming（内层 try-finally 负责清理）────────────
                try
                {
                    // Provider 内部驱动 FunctionInvokingChatClient + ChatClientAgent，
                    // 直接生成 StreamItem（含 token / thinking / tool_call / tool_result / usage）。
                    var responseAccumulator = new System.Text.StringBuilder();
                    await foreach (StreamItem item in chatProvider.StreamAgentAsync(
                                       chatCtx,
                                       messages,
                                       toolResult.AllTools,
                                       options: chatOptions,
                                       internalToolNames: SkillToolProvider.InternalToolNames,
                                       ct: ct))
                    {
                        anyItemWritten = true;
                        if (item is TokenItem tokenItem)
                            responseAccumulator.Append(tokenItem.Content);
                        await output.Writer.WriteAsync(item, ct);
                    }

                    succeeded = true;

                    // RAG 审计：fire-and-forget，不阻塞流式输出完成
                    if (_ragUsageAuditor is not null
                        && _ragRetrievalContext?.RetrievedChunks is { Count: > 0 } chunks
                        && responseAccumulator.Length > 0
                        && !string.IsNullOrWhiteSpace(sessionId))
                    {
                        string response = responseAccumulator.ToString();
                        string auditSessionId = sessionId;
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _ragUsageAuditor.AuditAsync(
                                    auditSessionId, chunks, response, CancellationToken.None);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "RAG 审计后台任务失败");
                            }
                        }, CancellationToken.None);
                    }
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
                    _devMetrics.RecordAgentRun(agent.Id, succeeded, runSw.ElapsedMilliseconds);
                    // TODO: Agent 月度预算（MonthlyBudgetUsd）检查已随 UsageTrackingMiddleware 一起撤除；
                    //       待 MicroChatContext + MicroProvider 接入预算策略后恢复。
                    if (!string.IsNullOrWhiteSpace(sessionId))
                        await _agentStatusNotifier.NotifyAsync(
                            sessionId, agent.Id, succeeded ? "completed" : "failed", CancellationToken.None);
                    if (ownsToolResult)
                        await toolResult.DisposeAsync();
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

                // 插件 Hook：SessionEnd
                if (_hookExecutor is not null)
                {
                    _ = _hookExecutor.ExecuteAsync(new HookContext
                    {
                        Event = HookEvent.SessionEnd,
                        SessionId = sessionId,
                        AgentId = agent.Id
                    }, CancellationToken.None);
                }
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

            // 插件 Hook：OnError
            if (_hookExecutor is not null)
            {
                _ = _hookExecutor.ExecuteAsync(new HookContext
                {
                    Event = HookEvent.OnError,
                    SessionId = sessionId,
                    ErrorMessage = ex.Message
                }, CancellationToken.None);
            }
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
        // 只允许 Chat 类型的 Provider 进入回退链，Embedding 模型不能用于对话
        IReadOnlyList<ProviderConfig> allProviders = _providerService.All
            .Where(p => p.ModelType != ModelType.Embedding)
            .ToList()
            .AsReadOnly();

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
        Agent? agent = _agentStore.GetAgentById(agentId);
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
        await using ToolCollectionResult toolResult = await _toolCollector.CollectToolsAsync(agent, context, ct);

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

    private async Task<List<ChatMessage>> BuildChatMessagesAsync(Agent agent, IReadOnlyList<SessionMessage> history, string? sessionId = null, string? skillCatalogFragment = null, string? behaviorSuffix = null, string? providerId = null, CancellationToken ct = default)
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
            // 计算初始裁切点，然后向后调整以确保 tool_call/tool_result 配对完整性
            int initialSplitIndex = history.Count - agent.ContextWindowMessages.Value;
            int adjustedSplitIndex = AdjustSplitIndexForToolCalls(history, initialSplitIndex);

            windowed = history.Skip(adjustedSplitIndex);

            // 触发异步溢出总结（fire-and-forget）
            if (_contextOverflowSummarizer is not null && !string.IsNullOrWhiteSpace(sessionId) && !string.IsNullOrWhiteSpace(providerId))
            {
                var overflowMessages = history.Take(adjustedSplitIndex).ToList();
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

            // 容错：若本组含 tool_call 但对应 tool_result 缺失（如会话中断、子代理写入竞争导致消息乱序），
            // 跳过整组，避免向 LLM 发送孤立的 tool_call 引发 "tool_call_id is not found" 错误。
            // 消息仍保留在 JSONL 中，不影响历史完整性。
            bool hasOrphanedToolCall = assistantContents.Any(c => c is FunctionCallContent)
                                       && toolContents.Count == 0;
            if (hasOrphanedToolCall)
            {
                _logger.LogWarning(
                    "跳过孤立 tool_call（无对应 tool_result），GroupId={GroupId}，Session={SessionId}",
                    groupId, sessionId);
                continue;
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

    /// <summary>
    /// Adjust the split index to ensure tool_call/tool_result pairs are not broken across
    /// the overflow boundary. If any tool_call in the overflow portion has its corresponding
    /// tool_result in the windowed portion, move the split index forward to include the
    /// tool_result (and everything in between) in the overflow.
    /// </summary>
    private static int AdjustSplitIndexForToolCalls(IReadOnlyList<SessionMessage> history, int initialSplitIndex)
    {
        if (initialSplitIndex <= 0 || initialSplitIndex >= history.Count)
            return initialSplitIndex;

        // Collect callIds from tool_call messages in the overflow portion [0, splitIndex)
        var pendingCallIds = new HashSet<string>();
        for (int i = 0; i < initialSplitIndex; i++)
        {
            SessionMessage msg = history[i];
            if (msg.MessageType == "tool_call" && msg.Metadata is not null
                && msg.Metadata.TryGetValue("callId", out var callIdEl))
            {
                string? callId = callIdEl.GetString();
                if (!string.IsNullOrEmpty(callId))
                    pendingCallIds.Add(callId);
            }
            // Remove callIds that have their tool_result also in the overflow portion
            if (msg.MessageType == "tool_result" && msg.Metadata is not null
                && msg.Metadata.TryGetValue("callId", out var resultIdEl))
            {
                string? resultId = resultIdEl.GetString();
                if (!string.IsNullOrEmpty(resultId))
                    pendingCallIds.Remove(resultId);
            }
        }

        // If no orphaned tool_calls, no adjustment needed
        if (pendingCallIds.Count == 0)
            return initialSplitIndex;

        // Scan forward from splitIndex to find the last matching tool_result
        int adjustedIndex = initialSplitIndex;
        for (int i = initialSplitIndex; i < history.Count && pendingCallIds.Count > 0; i++)
        {
            SessionMessage msg = history[i];
            if (msg.MessageType == "tool_result" && msg.Metadata is not null
                && msg.Metadata.TryGetValue("callId", out var callIdEl))
            {
                string? callId = callIdEl.GetString();
                if (!string.IsNullOrEmpty(callId) && pendingCallIds.Remove(callId))
                {
                    // Move split point past this tool_result
                    adjustedIndex = i + 1;
                }
            }
            // Also track tool_calls in the scanned region that might need their results
            if (msg.MessageType == "tool_call" && msg.Metadata is not null
                && msg.Metadata.TryGetValue("callId", out var newCallIdEl))
            {
                string? newCallId = newCallIdEl.GetString();
                if (!string.IsNullOrEmpty(newCallId))
                    pendingCallIds.Add(newCallId);
            }
        }

        return adjustedIndex;
    }

    /// <summary>通过 ChatContentRestorerService 将 SessionMessage 还原为 AIContent 列表。</summary>
    private List<AIContent> RestoreContents(SessionMessage msg) => _restorerService.RestoreContents(msg);

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
        Agent agent,
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
