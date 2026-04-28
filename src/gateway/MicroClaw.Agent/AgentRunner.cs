using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using MicroClaw.Agent.Dev;
using MicroClaw.Agent.Sessions;
using MicroClaw.Agent.Memory;
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
public sealed class AgentRunner : IService
{
    private readonly AgentStore _agentStore;
    private readonly ILogger<AgentRunner> _logger;
    private readonly ChatMessageAssembler _messageAssembler;
    private readonly ProviderService _providerService;
    private readonly ISessionService _sessionReader;
    private readonly SkillToolFactory _skillToolFactory;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IAgentStatusNotifier _agentStatusNotifier;
    private readonly ToolCollector _toolCollector;
    private readonly IDevMetricsService _devMetrics;
    private readonly IProviderRouter? _providerRouter;
    private readonly IHookExecutor? _hookExecutor;
    private readonly IRagUsageAuditor? _ragUsageAuditor;
    private readonly RagRetrievalContext? _ragRetrievalContext;
    
    public AgentRunner(IServiceProvider sp)
    {
        _agentStore = sp.GetRequiredService<AgentStore>();
        _loggerFactory = sp.GetRequiredService<ILoggerFactory>();
        _logger = _loggerFactory.CreateLogger<AgentRunner>();
        _messageAssembler = ActivatorUtilities.CreateInstance<ChatMessageAssembler>(sp);
        _providerService = sp.GetRequiredService<ProviderService>();
        _sessionReader = sp.GetRequiredService<ISessionService>();
        _skillToolFactory = sp.GetRequiredService<SkillToolFactory>();
        _agentStatusNotifier = sp.GetRequiredService<IAgentStatusNotifier>();
        _toolCollector = sp.GetRequiredService<ToolCollector>();
        _devMetrics = sp.GetRequiredService<IDevMetricsService>();
        _providerRouter = sp.GetService<IProviderRouter>();
        _hookExecutor = sp.GetService<IHookExecutor>();
        _ragUsageAuditor = sp.GetService<IRagUsageAuditor>();
        _ragRetrievalContext = sp.GetService<RagRetrievalContext>();
    }
    
    // ── IService ─────────────────────────────────────────────────────────
    public int InitOrder => 20;
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    
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
    
    private static string ResolveExecutionProviderId(MicroChatContext chatContext)
    {
        ArgumentNullException.ThrowIfNull(chatContext);
        
        if (!string.IsNullOrWhiteSpace(chatContext.TargetProviderId))
            return chatContext.TargetProviderId;
        
        throw new InvalidOperationException("Context-first AgentRunner path requires MicroChatContext.TargetProviderId to be populated.");
    }
    
    // ── 流式 ReAct 循环（AF ChatClientAgent + FunctionInvokingChatClient + Channel 事件桥接）──
    
    /// <summary>
    /// 主执行入口：调用方已通过 <see cref="MicroChatContext"/> 完成本次 dispatch 的装配，
    /// AgentRunner 仅消费 context 中的执行事实来驱动模型循环。
    /// </summary>
    public IAsyncEnumerable<StreamItem> StreamReActAsync(Agent agent, MicroChatContext chatContext, IReadOnlyList<string>? ancestorAgentIdsOverride = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(chatContext);
        
        var outputChannel = System.Threading.Channels.Channel.CreateUnbounded<StreamItem>();
        Task execution = ExecutePreparedStreamingCoreAsync(agent, chatContext, outputChannel, ancestorAgentIdsOverride);
        
        return ReadOutputAsync(outputChannel, execution, chatContext.Ct);
    }
    
    private async IAsyncEnumerable<StreamItem> ReadOutputAsync(System.Threading.Channels.Channel<StreamItem> outputChannel, Task execution, [EnumeratorCancellation] CancellationToken ct)
    {
        try
        {
            await foreach (StreamItem item in outputChannel.Reader.ReadAllAsync(ct))
                yield return item;
        }
        finally
        {
            try
            {
                await execution;
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
            }
        }
    }
    
    private async Task ExecutePreparedStreamingCoreAsync(Agent agent, MicroChatContext chatContext, System.Threading.Channels.Channel<StreamItem> output, IReadOnlyList<string>? ancestorAgentIdsOverride = null)
    {
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(chatContext);
        ArgumentNullException.ThrowIfNull(output);
        
        string primaryProviderId = ResolveExecutionProviderId(chatContext);
        IReadOnlyList<ProviderConfig> chain = BuildPreparedFallbackChain(chatContext);
        if (chain.Count == 0)
        {
            output.Writer.TryComplete(new InvalidOperationException($"Provider '{primaryProviderId}' not found or disabled."));
            return;
        }
        
        IReadOnlyList<ChatMessage> messages = RequirePreparedMessages(chatContext);
        IReadOnlyList<AITool> effectiveTools = RequirePreparedTools(chatContext);
        IReadOnlySet<string> internalToolNames = RequirePreparedInternalToolNames(chatContext);
        ChatOptions preparedOptions = RequirePreparedExecutionOptions(chatContext);
        string? sessionId = string.IsNullOrWhiteSpace(chatContext.Session.Id) ? null : chatContext.Session.Id;
        
        Exception? lastException = null;
        
        try
        {
            for (int attempt = 0; attempt < chain.Count; attempt++)
            {
                ProviderConfig provider = chain[attempt];
                bool isLastAttempt = attempt == chain.Count - 1;
                bool anyItemWritten = false;
                
                if (chatContext.Ct.IsCancellationRequested)
                {
                    output.Writer.TryComplete();
                    return;
                }
                
                if (attempt > 0)
                {
                    _logger.LogWarning("Provider '{PrimaryId}' failed, falling back to '{FallbackId}' (attempt {Attempt}/{Total})", chain[attempt - 1].Id, provider.Id, attempt + 1, chain.Count);
                }
                
                bool succeeded = false;
                Exception? streamingException = null;
                
                try
                {
                    chatContext.TargetAgentId ??= agent.Id;
                    chatContext.TargetAgentName ??= agent.Name;
                    chatContext.TargetProviderId = provider.Id;
                    
                    _logger.LogInformation("Agent {AgentId} streaming with {ToolCount} tools via provider {ProviderId}", agent.Id, effectiveTools.Count, provider.Id);
                    
                    ChatMicroProvider chatProvider = _providerService.TryGetProvider(provider.Id) ?? throw new InvalidOperationException($"Chat provider '{provider.Id}' is not available in cache.");
                    
                    ChatOptions chatOptions = preparedOptions;
                    
                    if (!string.IsNullOrWhiteSpace(sessionId))
                        await _agentStatusNotifier.NotifyAsync(sessionId, agent.Id, "running", chatContext.Ct);
                    
                    if (_hookExecutor is not null)
                    {
                        _ = _hookExecutor.ExecuteAsync(new HookContext { Event = HookEvent.SessionStart, SessionId = sessionId, AgentId = agent.Id }, CancellationToken.None);
                    }
                    
                    var runSw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var responseAccumulator = new System.Text.StringBuilder();
                        await foreach (StreamItem item in chatProvider.AgentStreamAsync(chatContext, messages, effectiveTools, options: chatOptions, internalToolNames: internalToolNames, ct: chatContext.Ct))
                        {
                            anyItemWritten = true;
                            if (item is TokenItem tokenItem)
                                responseAccumulator.Append(tokenItem.Content);
                            await output.Writer.WriteAsync(item, chatContext.Ct);
                        }
                        
                        succeeded = true;
                        
                        if (_ragUsageAuditor is not null && _ragRetrievalContext?.RetrievedChunks is { Count: > 0 } chunks && responseAccumulator.Length > 0 && !string.IsNullOrWhiteSpace(sessionId))
                        {
                            string response = responseAccumulator.ToString();
                            string auditSessionId = sessionId;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await _ragUsageAuditor.AuditAsync(auditSessionId, chunks, response, CancellationToken.None);
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
                        throw;
                    }
                    catch (Exception ex) when (!anyItemWritten && !isLastAttempt)
                    {
                        streamingException = ex;
                    }
                    finally
                    {
                        runSw.Stop();
                        _devMetrics.RecordAgentRun(agent.Id, succeeded, runSw.ElapsedMilliseconds);
                        if (!string.IsNullOrWhiteSpace(sessionId))
                            await _agentStatusNotifier.NotifyAsync(sessionId, agent.Id, succeeded ? "completed" : "failed", CancellationToken.None);
                    }
                    
                    if (streamingException is not null)
                    {
                        lastException = streamingException;
                        _logger.LogWarning(streamingException, "Provider '{ProviderId}' streaming failed without output (attempt {Attempt}/{Total}), will try fallback", provider.Id, attempt + 1, chain.Count);
                        continue;
                    }
                    
                    output.Writer.TryComplete();
                    
                    if (_hookExecutor is not null)
                    {
                        _ = _hookExecutor.ExecuteAsync(new HookContext { Event = HookEvent.SessionEnd, SessionId = sessionId, AgentId = agent.Id }, CancellationToken.None);
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
                    lastException = ex;
                    _logger.LogWarning(ex, "Provider '{ProviderId}' setup failed (attempt {Attempt}/{Total}), will try fallback", provider.Id, attempt + 1, chain.Count);
                }
            }
            
            output.Writer.TryComplete(lastException ?? new InvalidOperationException("All providers in fallback chain failed."));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ExecutePreparedStreamingCoreAsync 发生未处理异常，关闭 output Channel");
            output.Writer.TryComplete(ex);
            
            if (_hookExecutor is not null)
            {
                _ = _hookExecutor.ExecuteAsync(new HookContext { Event = HookEvent.OnError, SessionId = sessionId, ErrorMessage = ex.Message }, CancellationToken.None);
            }
        }
    }
    
    private static IReadOnlyList<ChatMessage> RequirePreparedMessages(MicroChatContext chatContext)
    {
        ArgumentNullException.ThrowIfNull(chatContext);
        
        if (chatContext.AssembledMessages is null)
            throw new InvalidOperationException("Context-first AgentRunner path requires MicroChatContext.AssembledMessages to be populated.");
        
        if (chatContext.AssembledMessages.Count == 0)
        {
            throw new InvalidOperationException("Dispatch message assembly produced an explicit empty message list; AgentRunner will not invoke the provider.");
        }
        
        return chatContext.AssembledMessages;
    }
    
    private static IReadOnlyList<AITool> RequirePreparedTools(MicroChatContext chatContext)
    {
        ArgumentNullException.ThrowIfNull(chatContext);
        
        if (chatContext.AssembledTools is not null)
            return chatContext.AssembledTools;
        
        throw new InvalidOperationException("Context-first AgentRunner path requires MicroChatContext.AssembledTools to be populated.");
    }
    
    private static IReadOnlySet<string> RequirePreparedInternalToolNames(MicroChatContext chatContext)
    {
        ArgumentNullException.ThrowIfNull(chatContext);
        
        return chatContext.InternalToolNames ?? throw new InvalidOperationException("Context-first AgentRunner path requires MicroChatContext.InternalToolNames to be populated.");
    }
    
    private static ChatOptions RequirePreparedExecutionOptions(MicroChatContext chatContext)
    {
        ArgumentNullException.ThrowIfNull(chatContext);
        
        return chatContext.ExecutionOptions ?? throw new InvalidOperationException("Context-first AgentRunner path requires MicroChatContext.ExecutionOptions to be populated.");
    }
    
    public async IAsyncEnumerable<StreamItem> StreamReActAsync(Agent agent, string providerId, IReadOnlyList<SessionMessage> history, string? sessionId = null, [EnumeratorCancellation] CancellationToken ct = default, string source = "chat", IReadOnlyList<string>? ancestorAgentIdsOverride = null, ToolCollectionResult? prebuiltTools = null, MicroChatContext? chatContext = null)
    {
        // ── 薄迭代器：delegating to non-iterator ExecuteStreamingCoreAsync ──────────
        // C# 规则：yield return 不能置于 try-catch 块中，因此将含回退逻辑的非迭代器方法
        // 通过 Channel 与迭代器解耦，迭代器只负责从 Channel 读取并 yield。
        var outputChannel = System.Threading.Channels.Channel.CreateUnbounded<StreamItem>();
        Task execution = ExecuteStreamingCoreAsync(agent, providerId, history, sessionId, ct, source, outputChannel, ancestorAgentIdsOverride, prebuiltTools, chatContext);
        
        try
        {
            await foreach (StreamItem item in outputChannel.Reader.ReadAllAsync(ct))
                yield return item;
        }
        finally
        {
            // 确保后台任务的任何异常被观察到（Channel 已 drain，不会重复 yield）
            try
            {
                await execution;
            }
            catch (OperationCanceledException)
            {
                /* 取消时静默 */
            }
            catch
            {
                /* 异常已通过 Channel 传播给调用方，此处忽略重复抛出 */
            }
        }
    }
    
    // ── 核心执行逻辑（非迭代器，可自由使用 try-catch）──────────────────────────
    
    /// <summary>
    /// 使用 Provider 回退链执行流式推理。失败且尚未产生任何输出时自动切换到下一个 Provider。
    /// 始终通过 <paramref name="output"/> Channel 完成（正常或带异常），供迭代器包装层读取。
    /// </summary>
    private async Task ExecuteStreamingCoreAsync(Agent agent, string primaryProviderId, IReadOnlyList<SessionMessage> history, string? sessionId, CancellationToken ct, string source, System.Threading.Channels.Channel<StreamItem> output, IReadOnlyList<string>? ancestorAgentIdsOverride = null, ToolCollectionResult? prebuiltTools = null, MicroChatContext? chatContext = null)
    {
        IReadOnlyList<ProviderConfig> chain = BuildFallbackChain(primaryProviderId, agent.RoutingStrategy, chatContext);
        
        if (chain.Count == 0)
        {
            output.Writer.TryComplete(new InvalidOperationException($"Provider '{primaryProviderId}' not found or disabled."));
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
                    _logger.LogWarning("Provider '{PrimaryId}' failed, falling back to '{FallbackId}' (attempt {Attempt}/{Total})", chain[attempt - 1].Id, provider.Id, attempt + 1, chain.Count);
                }
                
                // 执行状态标志
                bool succeeded = false;
                Exception? streamingException = null;
                
                try
                {
                    // ── 阶段 1：Setup ────────────────────────────────────────────
                    IReadOnlyList<ChatMessage>? assembledMessages = chatContext?.AssembledMessages;
                    bool usePreassembledMessages = assembledMessages is not null && attempt == 0 && (string.IsNullOrWhiteSpace(chatContext?.TargetProviderId) || string.Equals(chatContext?.TargetProviderId, provider.Id, StringComparison.Ordinal));
                    
                    SkillContext skillCtx;
                    List<ChatMessage> messages;
                    if (usePreassembledMessages)
                    {
                        skillCtx = _skillToolFactory.BuildSkillContext(agent.DisabledSkillIds, sessionId);
                        messages = [.. assembledMessages!];
                    }
                    else
                    {
                        ChatMessageAssemblyResult assembly = await _messageAssembler.AssembleAsync(agent, provider, history, sessionId, behaviorSuffix: chatContext?.PromptBehaviorSuffix, petKnowledge: chatContext?.PetKnowledge, ct);
                        skillCtx = assembly.SkillContext;
                        messages = [.. assembly.Messages];
                    }
                    
                    if (chatContext is not null)
                    {
                        chatContext.AssembledMessages = messages.AsReadOnly();
                        chatContext.TargetProviderId = provider.Id;
                    }
                    
                    if (messages.Count == 0)
                    {
                        throw new InvalidOperationException("Dispatch message assembly produced an explicit empty message list; AgentRunner will not invoke the provider.");
                    }
                    
                    // 工具收集（prebuiltTools != null 时由调用方管理释放，跳过内部收集）
                    ToolCollectionResult toolResult;
                    bool ownsToolResult;
                    if (chatContext?.AssembledTools is { Count: >= 0 })
                    {
                        if (prebuiltTools is not null)
                        {
                            toolResult = prebuiltTools;
                            ownsToolResult = false;
                        }
                        else
                        {
                            toolResult = new ToolCollectionResult();
                            toolResult.AddTools(chatContext.AssembledTools);
                            ownsToolResult = true;
                        }
                    }
                    else if (prebuiltTools is not null)
                    {
                        toolResult = prebuiltTools;
                        ownsToolResult = false;
                    }
                    else
                    {
                        IReadOnlyList<ToolGroupConfig>? toolOverrides = chatContext?.Items.TryGetValue(MicroChatContext.RuntimeToolGroupOverridesItemKey, out object? rawToolOverrides) == true && rawToolOverrides is IReadOnlyList<ToolGroupConfig> typedToolOverrides && typedToolOverrides.Count > 0 ? typedToolOverrides : null;
                        
                        Agent effectiveAgent = toolOverrides is { Count: > 0 } ? agent.WithToolOverrides(toolOverrides) : agent;
                        IMicroSession? sessionForTools = !string.IsNullOrWhiteSpace(sessionId) ? _sessionReader.Get(sessionId) : null;
                        
                        var ancestorAgentIds = new List<string>();
                        if (ancestorAgentIdsOverride is not null)
                        {
                            ancestorAgentIds.AddRange(ancestorAgentIdsOverride.Where(static id => !string.IsNullOrWhiteSpace(id)));
                        }
                        else if (SubAgentRunScope.Current?.AgentChain is { Count: > 0 } currentAgentChain)
                        {
                            ancestorAgentIds.AddRange(currentAgentChain);
                        }
                        
                        var toolContext = new ToolCreationContext(SessionId: sessionId, ChannelType: sessionForTools?.ChannelType, ChannelId: sessionForTools?.ChannelId, DisabledSkillIds: agent.DisabledSkillIds, CallingAgentId: agent.Id, AllowedSubAgentIds: chatContext?.HasRuntimeSubAgentAcl == true ? chatContext.RuntimeAllowedSubAgentIds : agent.AllowedSubAgentIds, AncestorAgentIds: chatContext?.AncestorAgentIds ?? (ancestorAgentIds.Count > 0 ? ancestorAgentIds : null));
                        toolResult = await _toolCollector.CollectToolsAsync(effectiveAgent, toolContext, ct);
                        ownsToolResult = true;
                    }
                    
                    IReadOnlyList<AITool> effectiveTools = chatContext?.AssembledTools ?? toolResult.AllTools;
                    IReadOnlySet<string> internalToolNames = chatContext?.InternalToolNames ?? SkillToolProvider.InternalToolNames;
                    
                    _logger.LogInformation("Agent {AgentId} streaming with {ToolCount} tools via provider {ProviderId}", agent.Id, effectiveTools.Count, provider.Id);
                    
                    ChatMicroProvider chatProvider = _providerService.TryGetProvider(provider.Id) ?? throw new InvalidOperationException($"Chat provider '{provider.Id}' is not available in cache.");
                    
                    bool usePreassembledExecutionOptions = chatContext?.ExecutionOptions is not null && attempt == 0 && (string.IsNullOrWhiteSpace(chatContext.TargetProviderId) || string.Equals(chatContext.TargetProviderId, provider.Id, StringComparison.Ordinal));
                    
                    ChatOptions chatOptions = usePreassembledExecutionOptions ? chatContext!.ExecutionOptions! : ChatExecutionOptionsFactory.Build(effectiveTools, provider, skillCtx.ModelOverride, skillCtx.EffortOverride, temperatureOverride: chatContext?.TemperatureOverride, topPOverride: chatContext?.TopPOverride);
                    
                    // MicroChatContext：Provider 内部依此归属 usage。
                    IMicroSession? sessionForCtx = !string.IsNullOrWhiteSpace(sessionId) ? _sessionReader.Get(sessionId) : null;
                    MicroChatContext effectiveChatContext = chatContext ?? (sessionForCtx is not null ? MicroChatContext.ForSystem(sessionForCtx, source, ct) : MicroChatContext.ForSystem(!string.IsNullOrWhiteSpace(sessionId) ? sessionId : $"agent:{agent.Id}", source, ct));
                    
                    effectiveChatContext.TargetAgentId ??= agent.Id;
                    effectiveChatContext.TargetAgentName ??= agent.Name;
                    effectiveChatContext.TargetProviderId = provider.Id;
                    
                    if (!string.IsNullOrWhiteSpace(sessionId))
                        await _agentStatusNotifier.NotifyAsync(sessionId, agent.Id, "running", ct);
                    
                    // 插件 Hook：SessionStart
                    if (_hookExecutor is not null)
                    {
                        _ = _hookExecutor.ExecuteAsync(new HookContext { Event = HookEvent.SessionStart, SessionId = sessionId, AgentId = agent.Id }, CancellationToken.None);
                    }
                    
                    var runSw = System.Diagnostics.Stopwatch.StartNew();
                    
                    // ── 阶段 2：Streaming（内层 try-finally 负责清理）────────────
                    try
                    {
                        // Provider 内部驱动 FunctionInvokingChatClient + ChatClientAgent，
                        // 直接生成 StreamItem（含 token / thinking / tool_call / tool_result / usage）。
                        var responseAccumulator = new System.Text.StringBuilder();
                        await foreach (StreamItem item in chatProvider.AgentStreamAsync(effectiveChatContext, messages, effectiveTools, options: chatOptions, internalToolNames: internalToolNames, ct: ct))
                        {
                            anyItemWritten = true;
                            if (item is TokenItem tokenItem)
                                responseAccumulator.Append(tokenItem.Content);
                            await output.Writer.WriteAsync(item, ct);
                        }
                        
                        succeeded = true;
                        
                        // RAG 审计：fire-and-forget，不阻塞流式输出完成
                        if (_ragUsageAuditor is not null && _ragRetrievalContext?.RetrievedChunks is { Count: > 0 } chunks && responseAccumulator.Length > 0 && !string.IsNullOrWhiteSpace(sessionId))
                        {
                            string response = responseAccumulator.ToString();
                            string auditSessionId = sessionId;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await _ragUsageAuditor.AuditAsync(auditSessionId, chunks, response, CancellationToken.None);
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
                            await _agentStatusNotifier.NotifyAsync(sessionId, agent.Id, succeeded ? "completed" : "failed", CancellationToken.None);
                        if (ownsToolResult)
                            await toolResult.DisposeAsync();
                    }
                    
                    // streamingException 被内层 catch 捕获 → 尝试下一个 Provider
                    if (streamingException is not null)
                    {
                        lastException = streamingException;
                        _logger.LogWarning(streamingException, "Provider '{ProviderId}' streaming failed without output (attempt {Attempt}/{Total}), will try fallback", provider.Id, attempt + 1, chain.Count);
                        continue;
                    }
                    
                    // 成功！完成输出 Channel
                    output.Writer.TryComplete();
                    
                    // 插件 Hook：SessionEnd
                    if (_hookExecutor is not null)
                    {
                        _ = _hookExecutor.ExecuteAsync(new HookContext { Event = HookEvent.SessionEnd, SessionId = sessionId, AgentId = agent.Id }, CancellationToken.None);
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
                    _logger.LogWarning(ex, "Provider '{ProviderId}' setup failed (attempt {Attempt}/{Total}), will try fallback", provider.Id, attempt + 1, chain.Count);
                    // 继续循环
                }
            }
            
            // 所有 Provider 均已耗尽
            output.Writer.TryComplete(lastException ?? new InvalidOperationException("All providers in fallback chain failed."));
            
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
                _ = _hookExecutor.ExecuteAsync(new HookContext { Event = HookEvent.OnError, SessionId = sessionId, ErrorMessage = ex.Message }, CancellationToken.None);
            }
        }
    }
    
    // ── Provider 回退链构建 ────────────────────────────────────────────────
    
    /// <summary>
    /// 构建 Provider 回退链：将指定 <paramref name="primaryProviderId"/> 排在链首，
    /// 其余按路由策略排序的已启用 Provider 依次跟随。
    /// 若未注册 <see cref="IProviderRouter"/>，则仅返回主 Provider（无回退）。
    /// </summary>
    private IReadOnlyList<ProviderConfig> BuildFallbackChain(string primaryProviderId, ProviderRoutingStrategy strategy, MicroChatContext? chatContext)
    {
        if (chatContext is not null)
        {
            List<string> preferredIds = [];
            string effectivePrimaryId = !string.IsNullOrWhiteSpace(chatContext.TargetProviderId) ? chatContext.TargetProviderId : primaryProviderId;
            
            if (!string.IsNullOrWhiteSpace(effectivePrimaryId))
                preferredIds.Add(effectivePrimaryId);
            
            if (chatContext.ProviderFallbackIds is { Count: > 0 })
            {
                preferredIds.AddRange(chatContext.ProviderFallbackIds.Where(static id => !string.IsNullOrWhiteSpace(id)));
            }
            
            if (preferredIds.Count > 0)
            {
                Dictionary<string, ProviderConfig> providersById = _providerService.All.Where(static provider => provider.IsEnabled && provider.ModelType == ModelType.Chat).GroupBy(static provider => provider.Id, StringComparer.Ordinal).ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
                
                List<ProviderConfig> plannedChain = [];
                HashSet<string> seenIds = new(StringComparer.Ordinal);
                foreach (string providerId in preferredIds)
                {
                    if (!seenIds.Add(providerId))
                        continue;
                    
                    if (providersById.TryGetValue(providerId, out ProviderConfig? provider))
                        plannedChain.Add(provider);
                }
                
                if (plannedChain.Count > 0)
                    return plannedChain.AsReadOnly();
            }
        }
        
        // 只允许 Chat 类型的 Provider 进入回退链，Embedding 模型不能用于对话
        IReadOnlyList<ProviderConfig> allProviders = _providerService.All.Where(p => p.ModelType != ModelType.Embedding).ToList().AsReadOnly();
        
        if (_providerRouter is null)
        {
            // 无路由器：仅使用主 Provider，不提供回退
            ProviderConfig? primary = allProviders.FirstOrDefault(p => p.Id == primaryProviderId && p.IsEnabled);
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
    
    private IReadOnlyList<ProviderConfig> BuildPreparedFallbackChain(MicroChatContext chatContext)
    {
        ArgumentNullException.ThrowIfNull(chatContext);
        
        List<string> preferredIds = [];
        if (!string.IsNullOrWhiteSpace(chatContext.TargetProviderId))
            preferredIds.Add(chatContext.TargetProviderId);
        
        if (chatContext.ProviderFallbackIds is { Count: > 0 })
        {
            preferredIds.AddRange(chatContext.ProviderFallbackIds.Where(static id => !string.IsNullOrWhiteSpace(id)));
        }
        
        if (preferredIds.Count == 0)
            return [];
        
        Dictionary<string, ProviderConfig> providersById = _providerService.All.Where(static provider => provider.IsEnabled && provider.ModelType == ModelType.Chat).GroupBy(static provider => provider.Id, StringComparer.Ordinal).ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);
        
        List<ProviderConfig> plannedChain = [];
        HashSet<string> seenIds = new(StringComparer.Ordinal);
        foreach (string providerId in preferredIds)
        {
            if (!seenIds.Add(providerId))
                continue;
            
            if (providersById.TryGetValue(providerId, out ProviderConfig? provider))
                plannedChain.Add(provider);
        }
        
        return plannedChain.AsReadOnly();
    }
    
    /// <summary>
    /// 调用指定 Agent 的工具（MCP 或内置），返回工具输出字符串。
    /// 通过 ToolCollector 统一收集后按名称查找。供工作流 Tool 节点使用。
    /// </summary>
    public async Task<string> InvokeToolAsync(string agentId, string toolName, IReadOnlyDictionary<string, string>? nodeConfig, string fallbackInput, CancellationToken ct)
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
        
        AITool? tool = toolResult.AllTools.FirstOrDefault(t => t.Name == toolName) ?? toolResult.AllTools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        
        if (tool is AIFunction fn)
        {
            object? result = await fn.InvokeAsync(new AIFunctionArguments(arguments), ct);
            return result?.ToString() ?? string.Empty;
        }
        
        _logger.LogWarning("InvokeToolAsync: Tool '{ToolName}' not found for Agent '{AgentId}'.", toolName, agentId);
        return fallbackInput;
    }
}