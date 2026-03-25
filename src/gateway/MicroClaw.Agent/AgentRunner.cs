using MicroClaw.Agent.Memory;
using MicroClaw.Channels;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers;
using MicroClaw.Skills;
using MicroClaw.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
namespace MicroClaw.Agent;

/// <summary>
/// Agent 执行引擎：实现 ReAct 循环（推理 → 工具调用 → 观察 → 循环）。
/// System Prompt 由 Agent DNA（SOUL + MEMORY）+ Session DNA（USER/AGENTS）+ Session 记忆（长期+每日权重衰减）构成。
/// MCP 工具从全局 McpServerConfigStore 加载，按 Agent.EnabledMcpServerIds 过滤。
/// 实现 IAgentMessageHandler，供渠道消息处理器路由调用。
/// </summary>
public sealed class AgentRunner(
    AgentStore agentStore,
    AgentDnaService agentDnaService,
    SessionDnaService sessionDnaService,
    MemoryService memoryService,
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
    IEnumerable<IBuiltinToolProvider> builtinToolProviders) : IAgentMessageHandler
{
    private readonly ILogger<AgentRunner> _logger = loggerFactory.CreateLogger<AgentRunner>();
    private readonly IReadOnlyList<IBuiltinToolProvider> _builtinToolProviders = builtinToolProviders.ToList().AsReadOnly();

    // ── IAgentMessageHandler ────────────────────────────────────────────────

    /// <summary>所有渠道消息默认路由到主 Agent（IsDefault=true），不再按渠道绑定匹配。</summary>
    public bool HasAgentForChannel(string channelId)
    {
        AgentConfig? main = agentStore.GetDefault();
        return main is { IsEnabled: true };
    }

    public async Task<string> HandleMessageAsync(
        string channelId,
        string sessionId,
        IReadOnlyList<SessionMessage> history,
        CancellationToken ct = default)
    {
        AgentConfig? agent = agentStore.GetDefault();
        if (agent is null || !agent.IsEnabled)
            throw new InvalidOperationException("No enabled default agent found.");

        // 从 session 获取 providerId；若 session 未绑定模型，回退到默认 Provider
        SessionInfo? session = sessionReader.Get(sessionId);
        string providerId = !string.IsNullOrWhiteSpace(session?.ProviderId)
            ? session.ProviderId
            : providerStore.GetDefault()?.Id ?? string.Empty;

        return await RunReActAsync(agent, providerId, history, sessionId, ct, source: "channel");
    }

    // ── 核心 ReAct 循环（非流式）────────────────────────────────────────────

    public async Task<string> RunReActAsync(
        AgentConfig agent,
        string providerId,
        IReadOnlyList<SessionMessage> history,
        string? sessionId = null,
        CancellationToken ct = default,
        string source = "subagent")
    {
        ProviderConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == providerId);
        if (provider is null || !provider.IsEnabled)
            throw new InvalidOperationException($"Provider '{providerId}' not found or disabled.");

        SkillContext skillCtx = skillToolFactory.BuildSkillContext(agent.BoundSkillIds, sessionId);
        List<ChatMessage> messages = BuildChatMessages(agent, history, sessionId, skillCtx.CatalogFragment);

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

        _logger.LogInformation("Agent {AgentId} loaded {ToolCount} tools ({McpCount} MCP + {BuiltinCount} built-in)",
            agent.Id, allTools.Count, mcpTools.Count, allTools.Count - mcpTools.Count);

        if (!string.IsNullOrWhiteSpace(sessionId))
            await agentStatusNotifier.NotifyAsync(sessionId, agent.Id, "running", ct);

        bool succeeded = false;
        try
        {
            IChatClient client = BuildClient(provider, allTools.Count > 0);
            ChatOptions chatOptions = BuildChatOptions(allTools, provider, skillCtx.ModelOverride, skillCtx.EffortOverride);
            ChatResponse response = await client.GetResponseAsync(messages, chatOptions, ct);

            await TrackUsageAsync(response.Usage, sessionId, provider, source, ct);

            succeeded = true;
            return response.Text ?? "（无回复）";
        }
        finally
        {
            await DisposeConnectionsAsync(connections);
            if (!string.IsNullOrWhiteSpace(sessionId))
                await agentStatusNotifier.NotifyAsync(sessionId, agent.Id, succeeded ? "completed" : "failed", CancellationToken.None);
        }
    }

    // ── 流式 ReAct 循环（供 SSE API 使用）──────────────────────────────────

    public async IAsyncEnumerable<string> StreamReActAsync(
        AgentConfig agent,
        string providerId,
        IReadOnlyList<SessionMessage> history,
        string? sessionId = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ProviderConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == providerId);
        if (provider is null || !provider.IsEnabled)
            throw new InvalidOperationException($"Provider '{providerId}' not found or disabled.");

        SkillContext skillCtx = skillToolFactory.BuildSkillContext(agent.BoundSkillIds, sessionId);
        List<ChatMessage> messages = BuildChatMessages(agent, history, sessionId, skillCtx.CatalogFragment);

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

        if (!string.IsNullOrWhiteSpace(sessionId))
            await agentStatusNotifier.NotifyAsync(sessionId, agent.Id, "running", ct);

        bool succeeded = false;
        UsageDetails? lastUsage = null;
        ChatResponseUpdate? lastUpdate = null;
        try
        {
            IChatClient client = BuildClient(provider, allTools.Count > 0);
            ChatOptions chatOptions = BuildChatOptions(allTools, provider, skillCtx.ModelOverride, skillCtx.EffortOverride);

            await foreach (ChatResponseUpdate update in
                client.GetStreamingResponseAsync(messages, chatOptions, ct))
            {
                // 0-B-4: 仅记录最后一个 update，不积累全量内容（usage 由提供商在末尾报告）
                lastUpdate = update;

                string token = update.Text ?? string.Empty;
                if (!string.IsNullOrEmpty(token))
                    yield return token;
            }

            // 从最后一个 update 获取 Usage
            if (lastUpdate is not null)
                lastUsage = new List<ChatResponseUpdate> { lastUpdate }.ToChatResponse().Usage;
            succeeded = true;
        }
        finally
        {
            await DisposeConnectionsAsync(connections);
            await TrackUsageAsync(lastUsage, sessionId, provider, "chat", CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(sessionId))
                await agentStatusNotifier.NotifyAsync(sessionId, agent.Id, succeeded ? "completed" : "failed", CancellationToken.None);
        }
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
                ToolGroupConfig? cfg = agent.ToolGroupConfigs.FirstOrDefault(g => g.GroupId == s.Name);
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

    private List<ChatMessage> BuildChatMessages(AgentConfig agent, IReadOnlyList<SessionMessage> history, string? sessionId = null, string? skillCatalogFragment = null)
    {
        string systemPrompt = BuildSystemPrompt(agent, sessionId, skillCatalogFragment);
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
            ChatRole role = msg.Role == "user" ? ChatRole.User : ChatRole.Assistant;
            messages.Add(new ChatMessage(role, msg.Content));
        }

        return messages;
    }

    /// <summary>
    /// 构建 System Prompt：Agent DNA（SOUL + MEMORY）→ Session DNA（USER + AGENTS）→ Session 记忆 → Skill Context。
    /// sessionId 为空时仅注入 Agent DNA（子代理场景）。
    /// </summary>
    internal string BuildSystemPrompt(AgentConfig agent, string? sessionId, string? skillContext = null)
    {
        var parts = new List<string>(5);

        // 1. Agent 级 DNA（SOUL + MEMORY，跨会话共享）
        string agentContext = agentDnaService.BuildAgentContext(agent.Id);
        if (!string.IsNullOrWhiteSpace(agentContext)) parts.Add(agentContext);

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            // 2. Session 级 DNA（USER + AGENTS）
            string sessionDna = sessionDnaService.BuildDnaContext(sessionId);
            if (!string.IsNullOrWhiteSpace(sessionDna)) parts.Add(sessionDna);

            // 3. Session 记忆（长期 + 每日权重衰减）
            string memoryContext = memoryService.BuildMemoryContext(sessionId);
            if (!string.IsNullOrWhiteSpace(memoryContext)) parts.Add(memoryContext);
        }

        // 4. Skill Context
        if (!string.IsNullOrWhiteSpace(skillContext)) parts.Add(skillContext);
        return string.Join("\n\n", parts);
    }

    private IChatClient BuildClient(ProviderConfig provider, bool withTools)
    {
        IChatClient inner = clientFactory.Create(provider);
        if (!withTools) return inner;

        // UseFunctionInvocation 中间件自动处理 Tool Call → Invoke → Observation 轮次
        // MaximumIterationsPerRequest=10 防止无限循环（默认为 int.MaxValue）
        return inner.AsBuilder()
            .UseFunctionInvocation(loggerFactory, configure: c => c.MaximumIterationsPerRequest = 10)
            .Build();
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
        if (tools.Count > 0)
            options.Tools = [.. tools];
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

    private async Task TrackUsageAsync(
        UsageDetails? usage,
        string? sessionId,
        ProviderConfig provider,
        string source,
        CancellationToken ct)
    {
        if (usage is null) return;
        long inputTokens = usage.InputTokenCount ?? 0L;
        long outputTokens = usage.OutputTokenCount ?? 0L;
        if (inputTokens <= 0 && outputTokens <= 0) return;

        try
        {
            await usageTracker.TrackAsync(
                sessionId,
                provider.Id,
                provider.DisplayName,
                source,
                inputTokens,
                outputTokens,
                provider.Capabilities.InputPricePerMToken,
                provider.Capabilities.OutputPricePerMToken,
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to track token usage for session {SessionId}", sessionId);
        }
    }
}
