using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using MicroClaw.Agent.ContextProviders;
using MicroClaw.Agent.Dev;
using MicroClaw.Agent.Sessions;
using MicroClaw.Agent.Middleware;
using MicroClaw.Agent.Streaming;
using MicroClaw.Agent.Streaming.Handlers;
using MicroClaw.Channels;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Gateway.Contracts.Streaming;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers;
using MicroClaw.Skills;
using MicroClaw.Tools;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace MicroClaw.Agent;

/// <summary>
/// Agent 执行引擎：实现 ReAct 循环（推理 → 工具调用 → 观察 → 循环）。
/// System Prompt 由各 <see cref="IAgentContextProvider"/> 按 Order 顺序聚合构成。
/// MCP 工具从全局 McpServerConfigStore 加载，按 Agent.EnabledMcpServerIds 过滤。
/// 实现 IAgentMessageHandler，供渠道消息处理器路由调用。
/// </summary>
public sealed class AgentRunner(
    AgentStore agentStore,
    IEnumerable<IAgentContextProvider> contextProviders,
    McpServerConfigStore mcpServerConfigStore,
    ProviderConfigStore providerStore,
    ProviderClientFactory clientFactory,
    ISessionReader sessionReader,
    SkillToolFactory skillToolFactory,
    SkillInvocationTool skillInvocationTool,
    IUsageTracker usageTracker,
    ILoggerFactory loggerFactory,
    IAgentStatusNotifier agentStatusNotifier,
    ChannelConfigStore channelConfigStore,
    IEnumerable<IChannelToolProvider> toolProviders,
    IEnumerable<IBuiltinToolProvider> builtinToolProviders,
    IDevMetricsService devMetrics,
    AIContentPipeline contentPipeline) : IAgentMessageHandler
{
    private readonly ILogger<AgentRunner> _logger = loggerFactory.CreateLogger<AgentRunner>();
    private readonly IReadOnlyList<IBuiltinToolProvider> _builtinToolProviders = builtinToolProviders.ToList().AsReadOnly();
    private readonly IReadOnlyList<IAgentContextProvider> _contextProviders =
        contextProviders.OrderBy(p => p.Order).ToList().AsReadOnly();

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

        // 从 session 获取 providerId；若 session 未绑定模型，回退到默认 Provider
        SessionInfo? session = sessionReader.Get(sessionId);
        string providerId = !string.IsNullOrWhiteSpace(session?.ProviderId)
            ? session.ProviderId
            : providerStore.GetDefault()?.Id ?? string.Empty;

        await foreach (StreamItem item in StreamReActAsync(agent, providerId, history, sessionId, ct, source: "channel"))
            yield return item;
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
        ProviderConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == providerId);
        if (provider is null || !provider.IsEnabled)
            throw new InvalidOperationException($"Provider '{providerId}' not found or disabled.");

        IReadOnlyList<SessionMessage> validatedHistory = ValidateModalities(history, provider);

        SkillContext skillCtx = skillToolFactory.BuildSkillContext(agent.BoundSkillIds, sessionId);
        List<ChatMessage> messages = await BuildChatMessagesAsync(agent, validatedHistory, sessionId, skillCtx.CatalogFragment, ct);

        // 按工具配置过滤要连接的 MCP Server
        IReadOnlyList<McpServerConfig> enabledMcpServers = GetEnabledMcpServers(agent);
        var (mcpTools, connections) = await ToolRegistry.LoadToolsAsync(enabledMcpServers, loggerFactory, ct);

        // 过滤 MCP 工具中被单独禁用的工具
        IEnumerable<McpClientTool> filteredMcpTools = FilterMcpTools(agent, mcpTools);

        // 追加内置工具（通过 IBuiltinToolProvider 自动注册，按 ToolGroupConfig 过滤）
        List<AITool> allTools = [.. filteredMcpTools];
        allTools.AddRange(CollectBuiltinTools(agent, sessionId));

        // 若 Agent 绑定了技能，注入 invoke_skill 工具（懒加载全文）
        if (agent.BoundSkillIds.Count > 0)
            allTools.Add(skillInvocationTool.Create(agent.BoundSkillIds, sessionId));

        // 按会话所在渠道类型注入对应的渠道专属工具
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            SessionInfo? sessionForTools = sessionReader.Get(sessionId);
            if (sessionForTools is not null)
            {
                ChannelConfig? channelCfg = channelConfigStore.GetById(sessionForTools.ChannelId);
                IChannelToolProvider? toolProvider = toolProviders.FirstOrDefault(p => p.ChannelType == sessionForTools.ChannelType);
                if (toolProvider is not null && channelCfg is not null)
                    allTools.AddRange(toolProvider.CreateToolsForChannel(channelCfg));
            }
        }

        _logger.LogInformation("Agent {AgentId} streaming with {ToolCount} tools ({McpCount} MCP + {BuiltinCount} built-in)",
            agent.Id, allTools.Count, mcpTools.Count, allTools.Count - mcpTools.Count);

        // ── AgentFactory：创建 ChatClientAgent + 事件 Channel ──────────────

        ChatOptions chatOptions = BuildChatOptions(allTools, provider, skillCtx.ModelOverride, skillCtx.EffortOverride);
        IChatClient rawClient = clientFactory.Create(provider);
        var (chatAgent, eventChannel, runOptions) = AgentFactory.Create(rawClient, agent.Name, chatOptions, loggerFactory, devMetrics: devMetrics);

        // ── 并发流合并：token 流（RunStreamingAsync）+ 工具事件（Channel）──

        if (!string.IsNullOrWhiteSpace(sessionId))
            await agentStatusNotifier.NotifyAsync(sessionId, agent.Id, "running", ct);

        // ── AF AgentSession：将 MicroClaw Session 元数据注入 StateBag 供中间件访问 ──
        AgentSession afSession = await chatAgent.CreateSessionAsync(ct);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            SessionInfo? sessionInfo = sessionReader.Get(sessionId);
            if (sessionInfo is not null)
                AgentSessionAdapter.PopulateStateBag(afSession.StateBag, sessionInfo);
        }
        // else
        // {
        //     afSession = await chatAgent.CreateSessionAsync(ct);
        // }

        bool succeeded = false;
        UsageCapture usageCapture = new();
        Task? runTask = null;
        var runSw = System.Diagnostics.Stopwatch.StartNew();
        UsageContentHandler.BindCapture(usageCapture);

        try
        {
            // 后台任务：运行 AF agent，通过 AIContentPipeline 将内容转换为 StreamItem 写入 eventChannel
            runTask = Task.Run(async () =>
            {
                try
                {
                    await foreach (AgentResponseUpdate update in chatAgent.RunStreamingAsync(messages, session: afSession, runOptions, ct))
                    {
                        foreach (StreamItem item in contentPipeline.Process(update.Contents))
                            await eventChannel.Writer.WriteAsync(item, ct);
                    }
                }
                catch (OperationCanceledException)
                {
                    // SSE 客户端断开或外部取消——静默结束
                }
                finally
                {
                    // 触发 ReadAllAsync 正常完成
                    eventChannel.Writer.TryComplete();
                }
            }, CancellationToken.None); // 不传 ct：让 finally 总能执行

            // 主流：从合并 Channel 中读取所有事件（token + 工具事件）
            await foreach (StreamItem item in eventChannel.Reader.ReadAllAsync(ct))
                yield return item;

            // 等待后台任务完成（任何异常在此重新抛出）
            await runTask;
            succeeded = true;
        }
        finally
        {
            runSw.Stop();
            devMetrics.RecordAgentRun(agent.Id, succeeded, runSw.ElapsedMilliseconds);
            await DisposeConnectionsAsync(connections);
            UsageContentHandler.UnbindCapture();
            await UsageTrackingMiddleware.TrackAsync(
                usageCapture, sessionId, provider, source,
                usageTracker, _logger, CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(sessionId))
                await agentStatusNotifier.NotifyAsync(sessionId, agent.Id, succeeded ? "completed" : "failed", CancellationToken.None);
        }
    }

    /// <summary>
    /// 调用指定 Agent 的工具（MCP 或内置），返回工具输出字符串。
    /// 先搜索 MCP 工具，未找到时回退搜索内置工具。供工作流 Tool 节点使用。
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

        // 1) 尝试 MCP 工具
        IReadOnlyList<McpServerConfig> servers = GetEnabledMcpServers(agent);
        if (servers.Count > 0)
        {
            var (mcpTools, connections) = await ToolRegistry.LoadToolsAsync(servers, loggerFactory, ct);
            try
            {
                McpClientTool? mcpTool = mcpTools.FirstOrDefault(t => t.Name == toolName)
                    ?? mcpTools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));

                if (mcpTool is not null)
                {
                    object? result = await mcpTool.InvokeAsync(new AIFunctionArguments(arguments), ct);
                    return result?.ToString() ?? string.Empty;
                }
            }
            finally
            {
                await DisposeConnectionsAsync(connections);
            }
        }

        // 2) 回退到内置工具
        List<AIFunction> builtinTools = CollectBuiltinTools(agent, sessionId: null).ToList();
        AIFunction? builtinTool = builtinTools.FirstOrDefault(t => t.Name == toolName)
            ?? builtinTools.FirstOrDefault(t => string.Equals(t.Name, toolName, StringComparison.OrdinalIgnoreCase));

        if (builtinTool is not null)
        {
            object? result = await builtinTool.InvokeAsync(new AIFunctionArguments(arguments), ct);
            return result?.ToString() ?? string.Empty;
        }

        _logger.LogWarning(
            "InvokeToolAsync: Tool '{ToolName}' not found (MCP or builtin) for Agent '{AgentId}'.",
            toolName, agentId);
        return fallbackInput;
    }

    // ── 私有辅助方法 ────────────────────────────────────────────────────────

    /// <summary>返回未被整体禁用的 MCP Server 配置列表（从全局库中按引用 ID 加载）。
    /// EnabledMcpServerIds 为空时，默认使用全局所有已启用的 MCP Server。</summary>
    private IReadOnlyList<McpServerConfig> GetEnabledMcpServers(AgentConfig agent)
    {
        // 空列表 = 默认启用全部已启用的 MCP Server（opt-out 模型）
        IReadOnlyList<McpServerConfig> servers = agent.EnabledMcpServerIds.Count == 0
            ? mcpServerConfigStore.AllEnabled
            : mcpServerConfigStore.GetEnabledByIds(agent.EnabledMcpServerIds);
        if (agent.ToolGroupConfigs.Count == 0) return servers;
        return servers
            .Where(s =>
            {
                // GroupId 可能存的是 srv.Name（旧格式）或 srv.Id（新格式），两者都兼容
                ToolGroupConfig? cfg = agent.ToolGroupConfigs
                    .FirstOrDefault(g => g.GroupId == s.Name || g.GroupId == s.Id);
                return cfg is null || cfg.IsEnabled;
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>从已加载的 MCP 工具中过滤掉被单独禁用的工具。</summary>
    private static IEnumerable<McpClientTool> FilterMcpTools(AgentConfig agent, IReadOnlyList<McpClientTool> tools)
    {
        if (agent.ToolGroupConfigs.Count == 0) return tools;
        // 保留未被任何启用分组明确禁用的工具
        return tools.Where(tool =>
        {
            bool isDisabledByAnyGroup = agent.ToolGroupConfigs
                .Any(g => g.IsEnabled && g.DisabledToolNames.Contains(tool.Name));
            return !isDisabledByAnyGroup;
        });
    }

    /// <summary>
    /// 遍历所有 IBuiltinToolProvider，按 Agent 的 ToolGroupConfig 过滤后返回工具列表。
    /// 不依赖 sessionId 的 Provider 忽略该参数；需要 sessionId 的 Provider 在其为空时自行返回空列表。
    /// </summary>
    private IEnumerable<AIFunction> CollectBuiltinTools(AgentConfig agent, string? sessionId)
    {
        foreach (IBuiltinToolProvider provider in _builtinToolProviders)
        {
            IReadOnlyList<AIFunction> tools = provider.CreateTools(sessionId);
            if (tools.Count == 0) continue;

            ToolGroupConfig? cfg = agent.ToolGroupConfigs.FirstOrDefault(g => g.GroupId == provider.GroupId);
            if (cfg is not null && !cfg.IsEnabled) continue;

            IEnumerable<AIFunction> filtered = cfg is null
                ? tools
                : tools.Where(t => !cfg.DisabledToolNames.Contains(t.Name));

            foreach (AIFunction tool in filtered)
                yield return tool;
        }
    }

    private async Task<List<ChatMessage>> BuildChatMessagesAsync(AgentConfig agent, IReadOnlyList<SessionMessage> history, string? sessionId = null, string? skillCatalogFragment = null, CancellationToken ct = default)
    {
        string systemPrompt = await BuildSystemPromptAsync(agent, sessionId, skillCatalogFragment, ct);
        if (!string.IsNullOrEmpty(systemPrompt))
        {
            int promptBytes = System.Text.Encoding.UTF8.GetByteCount(systemPrompt);
            _logger.LogDebug("System Prompt 注入：{Bytes} 字节（DNA+记忆），Session={SessionId}", promptBytes, sessionId);
        }

        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));

        // 应用滑动窗口：若配置了 ContextWindowMessages，只取最近 N 条消息传给 LLM
        IEnumerable<SessionMessage> windowed = agent.ContextWindowMessages.HasValue
            ? history.TakeLast(agent.ContextWindowMessages.Value)
            : history;

        foreach (SessionMessage msg in windowed)
        {
            // ── 工具调用：还原为 MEAI FunctionCallContent ──────────────────
            if (msg.MessageType == "tool_call" && msg.Metadata is not null)
            {
                string? callId = msg.Metadata.TryGetValue("callId", out var cidEl) ? cidEl.GetString() : null;
                string? toolName = msg.Metadata.TryGetValue("toolName", out var tnEl) ? tnEl.GetString() : null;
                if (callId is not null && toolName is not null)
                {
                    IDictionary<string, object?>? args = msg.Metadata.TryGetValue("arguments", out var argsEl)
                        && argsEl.ValueKind == System.Text.Json.JsonValueKind.Object
                        ? argsEl.Deserialize<Dictionary<string, object?>>() : null;
                    messages.Add(new ChatMessage(ChatRole.Assistant,
                        [new FunctionCallContent(callId, toolName, args)]));    
                }
                continue;
            }

            // ── 工具结果：还原为 MEAI FunctionResultContent ─────────────────
            if (msg.MessageType == "tool_result" && msg.Metadata is not null)
            {
                string? callId = msg.Metadata.TryGetValue("callId", out var cidEl) ? cidEl.GetString() : null;
                if (callId is not null)
                {
                    messages.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(callId, msg.Content)]));    
                }
                continue;
            }

            // ── 子 Agent / 其他系统消息：跳过 ────────────────────────────────
            if (msg.MessageType is "sub_agent_start" or "sub_agent_result")
                continue;
            if (msg.Role is "system" or "tool")
                continue;

            // ── 常规 user / assistant 消息 ───────────────────────────────────
            ChatRole role = msg.Role == "user" ? ChatRole.User : ChatRole.Assistant;

            if (msg.Attachments is { Count: > 0 })
            {
                var contents = new List<AIContent>();
                if (!string.IsNullOrEmpty(msg.Content))
                    contents.Add(new TextContent(msg.Content));

                foreach (MessageAttachment att in msg.Attachments)
                {
                    byte[] bytes = Convert.FromBase64String(att.Base64Data);
                    contents.Add(new DataContent(bytes, att.MimeType));
                }

                messages.Add(new ChatMessage(role, contents));
            }
            else
            {
                messages.Add(new ChatMessage(role, msg.Content));
            }
        }

        return messages;
    }

    /// <summary>
    /// 构建 System Prompt：按 <see cref="IAgentContextProvider.Order"/> 顺序聚合所有 Provider 的上下文片段，
    /// 末尾追加 Skill Context。
    /// </summary>
    internal async ValueTask<string> BuildSystemPromptAsync(AgentConfig agent, string? sessionId, string? skillContext = null, CancellationToken ct = default)
    {
        var parts = new List<string>(_contextProviders.Count + 1);

        foreach (IAgentContextProvider provider in _contextProviders)
        {
            string? fragment = await provider.BuildContextAsync(agent, sessionId, ct);
            if (!string.IsNullOrWhiteSpace(fragment))
                parts.Add(fragment);
        }

        // Skill Context 始终排在最后
        if (!string.IsNullOrWhiteSpace(skillContext))
            parts.Add(skillContext);

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
        string? effortOverride = null)
    {
        var options = new ChatOptions
        {
            ModelId = modelOverride ?? provider.ModelName,
            MaxOutputTokens = provider.MaxOutputTokens,
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

    private async Task DisposeConnectionsAsync(IAsyncDisposable[] connections)
    {
        foreach (IAsyncDisposable conn in connections)
        {
            try { await conn.DisposeAsync(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Error disposing MCP connection"); }
        }
    }
}
