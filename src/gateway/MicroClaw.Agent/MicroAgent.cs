using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Agent;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Agent.Dev;
using MicroClaw.Configuration.Options;
using MicroClaw.Core;
using MicroClaw.Core.Logging;
using MicroClaw.Plugins.Hooks;
using MicroClaw.Providers;
using MicroClaw.RAG;
using MicroClaw.Tools;
using MicroClaw.Utils;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent;
/// <summary>
/// Agent 运行时实体（有行为的聚合根）。
/// <para>
/// 继承 <see cref="MicroObject"/> 以接入 MicroClaw.Core 的组件模式；
/// 实现 <see cref="IMicroAgent"/> 对外暴露执行契约。
/// </para>
/// <para>
/// 运行时依赖（ProviderService、ToolCollector 等）在 <c>OnInitializedAsync</c> 中惰性解析；
/// 由 <see cref="MicroEngine"/> 生命周期统一驱动初始化与激活。
/// ReAct 执行逻辑在 P1-03 <c>StreamAsync</c> 中迁入；工具直调逻辑在 P1-04 迁入。
/// </para>
/// </summary>
public sealed class MicroAgent : MicroObject, IMicroAgent
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    
    private readonly IServiceProvider _sp;
    
    // ── 持久化数据 ────────────────────────────────────────────────────────
    
    private AgentEntityConfig _config;
    private List<string> _disabledSkillIds;
    private List<string> _disabledMcpServerIds;
    private List<ToolGroupConfig> _toolGroupConfigs;
    private List<string>? _allowedSubAgentIds;
    private ProviderRoutingStrategy _routingStrategy;
    
    // ── 运行时依赖（InitializeAsync 后填充，StreamAsync/InvokeToolAsync 前只读） ──
    
    private ProviderService? _providerService;
    private ToolCollector? _toolCollector;
    private IAgentStatusNotifier? _agentStatusNotifier;
    private IDevMetricsService? _devMetrics;
    private IProviderRouter? _providerRouter;
    private IHookExecutor? _hookExecutor;
    private IRagUsageAuditor? _ragUsageAuditor;
    private RagRetrievalContext? _ragRetrievalContext;
    
    public MicroAgent(AgentEntityConfig config, IServiceProvider sp)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(sp);
        _config = config;
        _sp = sp;
        _disabledSkillIds = MicroClawUtils.DeserializeList<string>(config.DisabledSkillIdsJson);
        _disabledMcpServerIds = MicroClawUtils.DeserializeList<string>(config.DisabledMcpServerIdsJson);
        _toolGroupConfigs = MicroClawUtils.DeserializeList<ToolGroupConfig>(config.ToolGroupConfigsJson);
        _allowedSubAgentIds = config.AllowedSubAgentIdsJson is null ? null : MicroClawUtils.DeserializeList<string>(config.AllowedSubAgentIdsJson);
        _routingStrategy = AgentUtils.ParseRoutingStrategy(config.RoutingStrategy);
    }
    
    // ── 工厂 ──────────────────────────────────────────────────────────────
    
    public static MicroAgent New(IServiceProvider sp, string name, string description, bool isEnabled = true, int contextWindowMessages = 10) =>
        new(new AgentEntityConfig
        {
            Id = MicroClawUtils.GetUniqueId(),
            Name = name,
            Description = description,
            IsEnabled = isEnabled,
            CreatedAtMs = TimeUtils.ToMs(DateTimeOffset.UtcNow),
            ContextWindowMessages = contextWindowMessages
        }, sp);
    
    // ── 持久化导出 / 克隆 / 替换 ─────────────────────────────────────────
    
    /// <summary>导出当前持久化数据快照（不复制运行时依赖）。</summary>
    public AgentEntityConfig ToConfig() => _config with { };
    
    /// <summary>创建脱离 MicroEngine 生命周期的同 SP 克隆，作为只读快照使用。</summary>
    internal MicroAgent Clone() => new(_config with { }, _sp);
    
    
    // ── 持久化字段访问（属性 + 行为方法）─────────────────────────────────
    
    /// <inheritdoc/>
    public string Id => _config.Id;
    
    /// <inheritdoc/>
    public string Name
    {
        get => _config.Name;
        set => _config.Name = value;
    }
    
    /// <inheritdoc/>
    public string Description
    {
        get => _config.Description;
        set => _config.Description = value;
    }
    
    /// <inheritdoc/>
    public bool IsEnabled
    {
        get => _config.IsEnabled;
        set => _config.IsEnabled = value;
    }
    
    /// <inheritdoc/>
    public bool IsDefault
    {
        get => _config.IsDefault;
        set => _config.IsDefault = value;
    }
    
    public DateTimeOffset CreatedAtUtc => TimeUtils.FromMs(_config.CreatedAtMs);
    
    /// <inheritdoc/>
    public int? ContextWindowMessages
    {
        get => _config.ContextWindowMessages;
        set => _config.ContextWindowMessages = value!.Value;
    }
    
    public ProviderRoutingStrategy RoutingStrategy
    {
        get => _routingStrategy;
        set
        {
            _routingStrategy = value;
            _config.RoutingStrategy = value == ProviderRoutingStrategy.Default ? null : value.ToString();
        }
    }
    
    public decimal? MonthlyBudgetUsd
    {
        get => _config.MonthlyBudgetUsd;
        set => _config.MonthlyBudgetUsd = value!.Value;
    }
    
    /// <inheritdoc/>
    public IReadOnlyList<string> DisabledSkillIds => _disabledSkillIds.AsReadOnly();
    
    /// <inheritdoc/>
    public IReadOnlyList<string> DisabledMcpServerIds => _disabledMcpServerIds.AsReadOnly();
    
    public IReadOnlyList<ToolGroupConfig> ToolGroupConfigs => _toolGroupConfigs.AsReadOnly();
    
    /// <inheritdoc/>
    public IReadOnlyList<string>? AllowedSubAgentIds => _allowedSubAgentIds?.AsReadOnly();
    
    // ── 行为方法：生命周期 ────────────────────────────────────────────────
    
    public void UpdateInfo(string name, string description)
    {
        Name = name;
        Description = description;
    }
    
    // ── 行为方法：Tool 权限检查 ──────────────────────────────────────────
    
    /// <inheritdoc/>
    public bool IsToolGroupEnabled(string groupId)
    {
        ToolGroupConfig? cfg = _toolGroupConfigs.FirstOrDefault(g => g.GroupId == groupId);
        return cfg is null || cfg.IsEnabled;
    }
    
    /// <inheritdoc/>
    public bool IsToolDisabled(string groupId, string toolName)
    {
        ToolGroupConfig? cfg = _toolGroupConfigs.FirstOrDefault(g => g.GroupId == groupId);
        return cfg is not null && cfg.DisabledToolNames.Contains(toolName);
    }
    
    public void UpdateToolGroupConfigs(IReadOnlyList<ToolGroupConfig> configs)
    {
        _toolGroupConfigs = [.. configs];
        _config.ToolGroupConfigsJson = _toolGroupConfigs.Count > 0 ? JsonSerializer.Serialize(_toolGroupConfigs, JsonOpts) : null;
    }
    
    // ── 行为方法：MCP/Skill 禁用管理 ─────────────────────────────────────
    
    /// <inheritdoc/>
    public bool IsMcpServerDisabled(string serverIdOrName) => _disabledMcpServerIds.Contains(serverIdOrName);
    
    public void UpdateDisabledMcpServerIds(IReadOnlyList<string> ids)
    {
        _disabledMcpServerIds = [.. ids];
        _config.DisabledMcpServerIdsJson = _disabledMcpServerIds.Count > 0 ? JsonSerializer.Serialize(_disabledMcpServerIds, JsonOpts) : null;
    }
    
    public bool IsSkillDisabled(string skillId) => _disabledSkillIds.Contains(skillId);
    
    public void UpdateDisabledSkillIds(IReadOnlyList<string> ids)
    {
        _disabledSkillIds = [.. ids];
        _config.DisabledSkillIdsJson = _disabledSkillIds.Count > 0 ? JsonSerializer.Serialize(_disabledSkillIds, JsonOpts) : null;
    }
    
    // ── 行为方法：SubAgent 权限 ───────────────────────────────────────────
    
    /// <summary>
    /// 检查是否允许调用指定子代理。
    /// null 白名单 = 允许调用所有；空列表 = 禁止调用任何；具体 ID 列表 = 仅允许指定 ID。
    /// </summary>
    public bool CanCallSubAgent(string agentId)
    {
        if (_allowedSubAgentIds is null) return true;
        if (_allowedSubAgentIds.Count == 0) return false;
        return _allowedSubAgentIds.Contains(agentId);
    }
    
    public void UpdateAllowedSubAgentIds(IReadOnlyList<string>? ids)
    {
        _allowedSubAgentIds = ids is null ? null : [.. ids];
        _config.AllowedSubAgentIdsJson = _allowedSubAgentIds is null ? null : JsonSerializer.Serialize(_allowedSubAgentIds, JsonOpts);
    }
    
    // ── 初始化 ────────────────────────────────────────────────────────────
    
    /// <summary>
    /// MicroClaw.Core 生命周期钩子：从 DI 容器惰性解析所有运行时依赖。
    /// 不在构造函数中执行以遵循 "no IO in ctor" 约定。
    /// </summary>
    protected override ValueTask OnInitializedAsync(CancellationToken cancellationToken = default)
    {
        _providerService = _sp.GetRequiredService<ProviderService>();
        _toolCollector = _sp.GetRequiredService<ToolCollector>();
        _agentStatusNotifier = _sp.GetRequiredService<IAgentStatusNotifier>();
        _devMetrics = _sp.GetRequiredService<IDevMetricsService>();
        _providerRouter = _sp.GetService<IProviderRouter>();
        _hookExecutor = _sp.GetService<IHookExecutor>();
        _ragUsageAuditor = _sp.GetService<IRagUsageAuditor>();
        _ragRetrievalContext = _sp.GetService<RagRetrievalContext>();
        return ValueTask.CompletedTask;
    }
    
    // ── IMicroAgent 执行方法 ───────────────────────────────────────────────
    
    /// <inheritdoc/>
    public IAsyncEnumerable<StreamItem> StreamAsync(MicroChatContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        
        var outputChannel = Channel.CreateUnbounded<StreamItem>();
        Task execution = StreamingCoreAsync(context, outputChannel);
        
        return ReadOutputAsync(outputChannel, execution, context.Ct);
    }
    
    private async IAsyncEnumerable<StreamItem> ReadOutputAsync(Channel<StreamItem> outputChannel, Task execution, [EnumeratorCancellation] CancellationToken ct)
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
    
    private async Task StreamingCoreAsync(MicroChatContext chatContext, Channel<StreamItem> output)
    {
        ArgumentNullException.ThrowIfNull(chatContext);
        ArgumentNullException.ThrowIfNull(output);
        
        string primaryProviderId = ResolveExecutionProviderId(chatContext);
        IReadOnlyList<ProviderEntity> chain = BuildPreparedFallbackChain(chatContext);
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
                ProviderEntity provider = chain[attempt];
                bool isLastAttempt = attempt == chain.Count - 1;
                bool anyItemWritten = false;
                
                if (chatContext.Ct.IsCancellationRequested)
                {
                    output.Writer.TryComplete();
                    return;
                }
                
                if (attempt > 0)
                    Logger!.LogWarning("Provider '{PrimaryId}' failed, falling back to '{FallbackId}' (attempt {Attempt}/{Total})", chain[attempt - 1].Id, provider.Id, attempt + 1, chain.Count);
                
                bool succeeded = false;
                Exception? streamingException = null;
                
                try
                {
                    chatContext.TargetAgentId ??= Id;
                    chatContext.TargetAgentName ??= Name;
                    chatContext.TargetProviderId = provider.Id;
                    
                    Logger!.LogInformation("Agent {AgentId} streaming with {ToolCount} tools via provider {ProviderId}", Id, effectiveTools.Count, provider.Id);
                    
                    ChatMicroProvider chatProvider = _providerService!.TryGetProvider(provider.Id) ?? throw new InvalidOperationException($"Chat provider '{provider.Id}' is not available in cache.");
                    
                    if (!string.IsNullOrWhiteSpace(sessionId))
                        await _agentStatusNotifier!.NotifyAsync(sessionId, Id, "running", chatContext.Ct);
                    
                    if (_hookExecutor is not null)
                        _ = _hookExecutor.ExecuteAsync(new HookContext { Event = HookEvent.SessionStart, SessionId = sessionId, AgentId = Id }, CancellationToken.None);
                    
                    var runSw = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        var responseAccumulator = new System.Text.StringBuilder();
                        await foreach (StreamItem item in chatProvider.AgentStreamAsync(chatContext, messages, effectiveTools, options: preparedOptions, internalToolNames: internalToolNames, ct: chatContext.Ct))
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
                                    Logger!.LogWarning(ex, "RAG 审计后台任务失败");
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
                        _devMetrics!.RecordAgentRun(Id, succeeded, runSw.ElapsedMilliseconds);
                        if (!string.IsNullOrWhiteSpace(sessionId))
                            await _agentStatusNotifier!.NotifyAsync(sessionId, Id, succeeded ? "completed" : "failed", CancellationToken.None);
                    }
                    
                    if (streamingException is not null)
                    {
                        lastException = streamingException;
                        Logger!.LogWarning(streamingException, "Provider '{ProviderId}' streaming failed without output (attempt {Attempt}/{Total}), will try fallback", provider.Id, attempt + 1, chain.Count);
                        continue;
                    }
                    
                    output.Writer.TryComplete();
                    
                    if (_hookExecutor is not null)
                        _ = _hookExecutor.ExecuteAsync(new HookContext { Event = HookEvent.SessionEnd, SessionId = sessionId, AgentId = Id }, CancellationToken.None);
                    
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
                    Logger!.LogWarning(ex, "Provider '{ProviderId}' setup failed (attempt {Attempt}/{Total}), will try fallback", provider.Id, attempt + 1, chain.Count);
                }
            }
            
            output.Writer.TryComplete(lastException ?? new InvalidOperationException("All providers in fallback chain failed."));
        }
        catch (Exception ex)
        {
            Logger!.LogError(ex, "StreamingCoreAsync 遭遇未预期异常，关闭 output Channel");
            output.Writer.TryComplete(ex);
            
            if (_hookExecutor is not null)
                _ = _hookExecutor.ExecuteAsync(new HookContext { Event = HookEvent.OnError, SessionId = sessionId, ErrorMessage = ex.Message }, CancellationToken.None);
        }
    }
    
    // ── StreamAsync 辅助方法 ──────────────────────────────────────────────
    
    private static string ResolveExecutionProviderId(MicroChatContext chatContext)
    {
        ArgumentNullException.ThrowIfNull(chatContext);
        
        if (!string.IsNullOrWhiteSpace(chatContext.TargetProviderId))
            return chatContext.TargetProviderId;
        
        throw new InvalidOperationException("Context-first MicroAgent.StreamAsync requires MicroChatContext.TargetProviderId to be populated.");
    }
    
    private IReadOnlyList<ProviderEntity> BuildPreparedFallbackChain(MicroChatContext chatContext)
    {
        ArgumentNullException.ThrowIfNull(chatContext);
        
        List<string> preferredIds = [];
        if (!string.IsNullOrWhiteSpace(chatContext.TargetProviderId))
            preferredIds.Add(chatContext.TargetProviderId);
        
        if (chatContext.ProviderFallbackIds is { Count: > 0 })
            preferredIds.AddRange(chatContext.ProviderFallbackIds.Where(static id => !string.IsNullOrWhiteSpace(id)));
        
        if (preferredIds.Count == 0)
            return [];
        
        Dictionary<string, ProviderEntity> providersById = _providerService!.All.Where(static p => p.IsEnabled && p.ModelType == ModelType.Chat).GroupBy(static p => p.Id, StringComparer.Ordinal).ToDictionary(static g => g.Key, static g => g.First(), StringComparer.Ordinal);
        
        List<ProviderEntity> plannedChain = [];
        HashSet<string> seenIds = new(StringComparer.Ordinal);
        foreach (string providerId in preferredIds)
        {
            if (!seenIds.Add(providerId)) continue;
            if (providersById.TryGetValue(providerId, out ProviderEntity? p))
                plannedChain.Add(p);
        }
        
        return plannedChain.AsReadOnly();
    }
    
    private static IReadOnlyList<ChatMessage> RequirePreparedMessages(MicroChatContext chatContext)
    {
        ArgumentNullException.ThrowIfNull(chatContext);
        
        if (chatContext.AssembledMessages is null)
            throw new InvalidOperationException("Context-first MicroAgent.StreamAsync requires MicroChatContext.AssembledMessages to be populated.");
        
        if (chatContext.AssembledMessages.Count == 0)
            throw new InvalidOperationException("Dispatch message assembly produced an explicit empty message list; MicroAgent will not invoke the provider.");
        
        return chatContext.AssembledMessages;
    }
    
    private static IReadOnlyList<AITool> RequirePreparedTools(MicroChatContext chatContext)
    {
        ArgumentNullException.ThrowIfNull(chatContext);
        
        if (chatContext.AssembledTools is not null)
            return chatContext.AssembledTools;
        
        throw new InvalidOperationException("Context-first MicroAgent.StreamAsync requires MicroChatContext.AssembledTools to be populated.");
    }
    
    private static IReadOnlySet<string> RequirePreparedInternalToolNames(MicroChatContext chatContext)
    {
        ArgumentNullException.ThrowIfNull(chatContext);
        
        return chatContext.InternalToolNames ?? throw new InvalidOperationException("Context-first MicroAgent.StreamAsync requires MicroChatContext.InternalToolNames to be populated.");
    }
    
    private static ChatOptions RequirePreparedExecutionOptions(MicroChatContext chatContext)
    {
        ArgumentNullException.ThrowIfNull(chatContext);
        
        return chatContext.ExecutionOptions ?? throw new InvalidOperationException("Context-first MicroAgent.StreamAsync requires MicroChatContext.ExecutionOptions to be populated.");
    }
    
    // ── P1-04 InvokeToolAsync ─────────────────────────────────────────────
    
    /// <inheritdoc/>
    public async Task<string> InvokeToolAsync(string toolName, IReadOnlyDictionary<string, string>? args, string fallbackInput, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        
        // Build arguments: if caller provides none, fall back to { "input": fallbackInput }
        var arguments = new Dictionary<string, object?>();
        if (args is not null)
        {
            foreach (var kv in args)
                arguments[kv.Key] = kv.Value;
        }
        if (arguments.Count == 0)
            arguments["input"] = fallbackInput;
        
        var toolContext = new ToolCreationContext(CallingAgentId: Id);
        await using ToolCollectionResult toolResult = await _toolCollector!.CollectToolsAsync(this, toolContext, ct);
        
        AITool? tool = toolResult.AllTools.FirstOrDefault(t => t.Name == toolName) ?? toolResult.AllTools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));
        
        if (tool is AIFunction fn)
        {
            object? result = await fn.InvokeAsync(new AIFunctionArguments(arguments), ct);
            return result?.ToString() ?? string.Empty;
        }
        
        Logger!.LogWarning("InvokeToolAsync: Tool '{ToolName}' not found for Agent '{AgentId}'.", toolName, Id);
        return fallbackInput;
    }
}