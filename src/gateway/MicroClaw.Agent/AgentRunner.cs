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
/// Agent ִ�����棺ʵ�� ReAct ѭ�������� �� ���ߵ��� �� �۲� �� ѭ������
/// System Prompt �ɸ� <see cref="IAgentContextProvider"/> �� Order ˳��ۺϹ��ɡ�
/// MCP ���ߴ�ȫ�� McpServerConfigStore ���أ��� Agent.DisabledMcpServerIds �ų���
/// ʵ�� IAgentMessageHandler����������Ϣ������·�ɵ��á�
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
    
    // ���� IService ������������������������������������������������������������������������������������������������������������������
    public int InitOrder => 20;
    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    
    // ���� Provider ·�ɲ��Ը��� ����������������������������������������������������������������������������������������������
    
    /// <summary>
    /// �� Session δ��ʽ�� Provider ʱ���� Agent ·�ɲ��Դ������� Provider ���Զ�ѡ��
    /// ��������<see cref="IProviderRouter"/> �� <see cref="ProviderService.GetDefault"/> �� ���ַ�����
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
    
    // ���� ��ʽ ReAct ѭ����AF ChatClientAgent + FunctionInvokingChatClient + Channel �¼��Žӣ�����
    
    /// <summary>
    /// ��ִ����ڣ����÷���ͨ�� <see cref="MicroChatContext"/> ��ɱ��� dispatch ��װ�䣬
    /// AgentRunner ������ context �е�ִ����ʵ������ģ��ѭ����
    /// </summary>
    public IAsyncEnumerable<StreamItem> StreamReActAsync(AgentDto agent, MicroChatContext chatContext, IReadOnlyList<string>? ancestorAgentIdsOverride = null)
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
    
    private async Task ExecutePreparedStreamingCoreAsync(AgentDto agent, MicroChatContext chatContext, System.Threading.Channels.Channel<StreamItem> output, IReadOnlyList<string>? ancestorAgentIdsOverride = null)
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
                                    _logger.LogWarning(ex, "RAG ��ƺ�̨����ʧ��");
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
            _logger.LogError(ex, "ExecutePreparedStreamingCoreAsync ����δ�����쳣���ر� output Channel");
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
    
    public async IAsyncEnumerable<StreamItem> StreamReActAsync(AgentDto agent, string providerId, IReadOnlyList<SessionMessage> history, string? sessionId = null, [EnumeratorCancellation] CancellationToken ct = default, string source = "chat", IReadOnlyList<string>? ancestorAgentIdsOverride = null, ToolCollectionResult? prebuiltTools = null, MicroChatContext? chatContext = null)
    {
        // ���� ����������delegating to non-iterator ExecuteStreamingCoreAsync ��������������������
        // C# ����yield return �������� try-catch ���У���˽��������߼��ķǵ���������
        // ͨ�� Channel ����������������ֻ����� Channel ��ȡ�� yield��
        var outputChannel = System.Threading.Channels.Channel.CreateUnbounded<StreamItem>();
        Task execution = ExecuteStreamingCoreAsync(agent, providerId, history, sessionId, ct, source, outputChannel, ancestorAgentIdsOverride, prebuiltTools, chatContext);
        
        try
        {
            await foreach (StreamItem item in outputChannel.Reader.ReadAllAsync(ct))
                yield return item;
        }
        finally
        {
            // ȷ����̨������κ��쳣���۲쵽��Channel �� drain�������ظ� yield��
            try
            {
                await execution;
            }
            catch (OperationCanceledException)
            {
                /* ȡ��ʱ��Ĭ */
            }
            catch
            {
                /* �쳣��ͨ�� Channel ���������÷����˴������ظ��׳� */
            }
        }
    }
    
    // ���� ����ִ���߼����ǵ�������������ʹ�� try-catch������������������������������������������������������
    
    /// <summary>
    /// ʹ�� Provider ������ִ����ʽ�����ʧ������δ�����κ����ʱ�Զ��л�����һ�� Provider��
    /// ʼ��ͨ�� <paramref name="output"/> Channel ��ɣ���������쳣��������������װ���ȡ��
    /// </summary>
    private async Task ExecuteStreamingCoreAsync(AgentDto agent, string primaryProviderId, IReadOnlyList<SessionMessage> history, string? sessionId, CancellationToken ct, string source, System.Threading.Channels.Channel<StreamItem> output, IReadOnlyList<string>? ancestorAgentIdsOverride = null, ToolCollectionResult? prebuiltTools = null, MicroChatContext? chatContext = null)
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
                
                // ִ��״̬��־
                bool succeeded = false;
                Exception? streamingException = null;
                
                try
                {
                    // ���� �׶� 1��Setup ����������������������������������������������������������������������������������������
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
                    
                    // �����ռ���prebuiltTools != null ʱ�ɵ��÷������ͷţ������ڲ��ռ���
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
                        
                        AgentDto effectiveAgent = toolOverrides is { Count: > 0 } ? agent.WithToolOverrides(toolOverrides) : agent;
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
                    
                    // MicroChatContext��Provider �ڲ����˹��� usage��
                    IMicroSession? sessionForCtx = !string.IsNullOrWhiteSpace(sessionId) ? _sessionReader.Get(sessionId) : null;
                    MicroChatContext effectiveChatContext = chatContext ?? (sessionForCtx is not null ? MicroChatContext.ForSystem(sessionForCtx, source, ct) : MicroChatContext.ForSystem(!string.IsNullOrWhiteSpace(sessionId) ? sessionId : $"agent:{agent.Id}", source, ct));
                    
                    effectiveChatContext.TargetAgentId ??= agent.Id;
                    effectiveChatContext.TargetAgentName ??= agent.Name;
                    effectiveChatContext.TargetProviderId = provider.Id;
                    
                    if (!string.IsNullOrWhiteSpace(sessionId))
                        await _agentStatusNotifier.NotifyAsync(sessionId, agent.Id, "running", ct);
                    
                    // ��� Hook��SessionStart
                    if (_hookExecutor is not null)
                    {
                        _ = _hookExecutor.ExecuteAsync(new HookContext { Event = HookEvent.SessionStart, SessionId = sessionId, AgentId = agent.Id }, CancellationToken.None);
                    }
                    
                    var runSw = System.Diagnostics.Stopwatch.StartNew();
                    
                    // ���� �׶� 2��Streaming���ڲ� try-finally ���������������������������������
                    try
                    {
                        // Provider �ڲ����� FunctionInvokingChatClient + ChatClientAgent��
                        // ֱ������ StreamItem���� token / thinking / tool_call / tool_result / usage����
                        var responseAccumulator = new System.Text.StringBuilder();
                        await foreach (StreamItem item in chatProvider.AgentStreamAsync(effectiveChatContext, messages, effectiveTools, options: chatOptions, internalToolNames: internalToolNames, ct: ct))
                        {
                            anyItemWritten = true;
                            if (item is TokenItem tokenItem)
                                responseAccumulator.Append(tokenItem.Content);
                            await output.Writer.WriteAsync(item, ct);
                        }
                        
                        succeeded = true;
                        
                        // RAG ��ƣ�fire-and-forget����������ʽ������
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
                                    _logger.LogWarning(ex, "RAG ��ƺ�̨����ʧ��");
                                }
                            }, CancellationToken.None);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // ȡ��ֱ�����ϴ����������� OperationCanceledException catch ����
                    }
                    catch (Exception ex) when (!anyItemWritten && !isLastAttempt)
                    {
                        // ��ʽִ��ʧ�ܣ�����δ�����д���κ����� �� �ɰ�ȫ����
                        streamingException = ex;
                    }
                    finally
                    {
                        runSw.Stop();
                        _devMetrics.RecordAgentRun(agent.Id, succeeded, runSw.ElapsedMilliseconds);
                        // TODO: Agent �¶�Ԥ�㣨MonthlyBudgetUsd��������� UsageTrackingMiddleware һ�𳷳���
                        //       �� MicroChatContext + MicroProvider ����Ԥ����Ժ�ָ���
                        if (!string.IsNullOrWhiteSpace(sessionId))
                            await _agentStatusNotifier.NotifyAsync(sessionId, agent.Id, succeeded ? "completed" : "failed", CancellationToken.None);
                        if (ownsToolResult)
                            await toolResult.DisposeAsync();
                    }
                    
                    // streamingException ���ڲ� catch ���� �� ������һ�� Provider
                    if (streamingException is not null)
                    {
                        lastException = streamingException;
                        _logger.LogWarning(streamingException, "Provider '{ProviderId}' streaming failed without output (attempt {Attempt}/{Total}), will try fallback", provider.Id, attempt + 1, chain.Count);
                        continue;
                    }
                    
                    // �ɹ��������� Channel
                    output.Writer.TryComplete();
                    
                    // ��� Hook��SessionEnd
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
                    // Setup �׶�ʧ�ܣ�Provider ��δ����������� ������һ��
                    lastException = ex;
                    _logger.LogWarning(ex, "Provider '{ProviderId}' setup failed (attempt {Attempt}/{Total}), will try fallback", provider.Id, attempt + 1, chain.Count);
                    // ����ѭ��
                }
            }
            
            // ���� Provider ���Ѻľ�
            output.Writer.TryComplete(lastException ?? new InvalidOperationException("All providers in fallback chain failed."));
            
        } // end try
        catch (Exception ex)
        {
            // ��ȫ����ȷ�� output Channel ���κ�δԤ�ϵ��쳣·���¶�����ȷ�رգ�
            // �������Ѷ� ReadAllAsync() ���޹���
            _logger.LogError(ex, "ExecuteStreamingCoreAsync ����δ�����쳣���ر� output Channel");
            output.Writer.TryComplete(ex);
            
            // ��� Hook��OnError
            if (_hookExecutor is not null)
            {
                _ = _hookExecutor.ExecuteAsync(new HookContext { Event = HookEvent.OnError, SessionId = sessionId, ErrorMessage = ex.Message }, CancellationToken.None);
            }
        }
    }
    
    // ���� Provider ���������� ������������������������������������������������������������������������������������������������
    
    /// <summary>
    /// ���� Provider ����������ָ�� <paramref name="primaryProviderId"/> �������ף�
    /// ���ఴ·�ɲ�������������� Provider ���θ��档
    /// ��δע�� <see cref="IProviderRouter"/>����������� Provider���޻��ˣ���
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
        
        // ֻ���� Chat ���͵� Provider �����������Embedding ģ�Ͳ������ڶԻ�
        IReadOnlyList<ProviderConfig> allProviders = _providerService.All.Where(p => p.ModelType != ModelType.Embedding).ToList().AsReadOnly();
        
        if (_providerRouter is null)
        {
            // ��·��������ʹ���� Provider�����ṩ����
            ProviderConfig? primary = allProviders.FirstOrDefault(p => p.Id == primaryProviderId && p.IsEnabled);
            return primary is not null ? [primary] : [];
        }
        
        IReadOnlyList<ProviderConfig> orderedChain = _providerRouter.GetFallbackChain(allProviders, strategy);
        
        // �� primaryProviderId �������ף�����ʹ�� Session/Agent ָ���� Provider��
        ProviderConfig? primaryInChain = orderedChain.FirstOrDefault(p => p.Id == primaryProviderId);
        if (primaryInChain is null || string.IsNullOrWhiteSpace(primaryProviderId))
        {
            // �� Provider δ�ҵ���δָ����ֱ��ʹ�ò���˳��
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
    /// ����ָ�� Agent �Ĺ��ߣ�MCP �����ã������ع�������ַ�����
    /// ͨ�� ToolCollector ͳһ�ռ������Ʋ��ҡ��������� Tool �ڵ�ʹ�á�
    /// </summary>
    public async Task<string> InvokeToolAsync(string agentId, string toolName, IReadOnlyDictionary<string, string>? nodeConfig, string fallbackInput, CancellationToken ct)
    {
        AgentDto? agent = _agentStore.GetById(agentId);
        if (agent is null || !agent.IsEnabled)
        {
            _logger.LogWarning("InvokeToolAsync: Agent '{AgentId}' not found or disabled.", agentId);
            return fallbackInput;
        }
        
        // �������߲���
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
        
        // ͨ�� ToolCollector ͳһ�ռ������ҹ���
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