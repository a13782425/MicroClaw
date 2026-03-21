using MicroClaw.Agent.Memory;
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
/// 注入 DNA 记忆作为 SystemPrompt 上下文，通过 MCP 协议接入 Python/Node.js 工具。
/// 实现 IAgentMessageHandler，供渠道消息处理器路由调用。
/// </summary>
public sealed class AgentRunner(
    AgentStore agentStore,
    DNAService dnaService,
    ProviderConfigStore providerStore,
    ProviderClientFactory clientFactory,
    ISessionReader sessionReader,
    CronJobStore cronJobStore,
    ICronJobScheduler cronScheduler,
    SkillToolFactory skillToolFactory,
    ISubAgentRunner subAgentRunner,
    IUsageTracker usageTracker,
    ILoggerFactory loggerFactory,
    IAgentStatusNotifier agentStatusNotifier) : IAgentMessageHandler
{
    private readonly ILogger<AgentRunner> _logger = loggerFactory.CreateLogger<AgentRunner>();

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

        // 从 session 获取 providerId
        SessionInfo? session = sessionReader.Get(sessionId);
        string providerId = session?.ProviderId ?? string.Empty;

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

        List<ChatMessage> messages = BuildChatMessages(agent, history);

        // 按工具配置过滤要连接的 MCP Server
        IReadOnlyList<McpServerConfig> enabledMcpServers = FilterMcpServers(agent);
        var (mcpTools, connections) = await ToolRegistry.LoadToolsAsync(enabledMcpServers, loggerFactory, ct);

        // 过滤 MCP 工具中被单独禁用的工具
        IEnumerable<McpClientTool> filteredMcpTools = FilterMcpTools(agent, mcpTools);

        // 追加内置工具（当有 sessionId，且对应分组未禁用时）
        List<AITool> allTools = [.. filteredMcpTools];
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            allTools.AddRange(FilterCronTools(agent, CronTools.CreateForSession(sessionId, cronJobStore, cronScheduler)));
            // 追加技能工具
            allTools.AddRange(skillToolFactory.CreateTools(agent.BoundSkillIds, sessionId));
            // 追加子代理工具
            allTools.AddRange(SubAgentTools.CreateForSession(sessionId, agentStore, subAgentRunner));
        }
        else
        {
            _logger.LogDebug("sessionId 为空，跳过 CronJob/Skill/SubAgent 工具加载，Agent={AgentId}", agent.Id);
        }

        _logger.LogInformation("Agent {AgentId} loaded {ToolCount} tools ({McpCount} MCP + {CronCount} built-in)",
            agent.Id, allTools.Count, mcpTools.Count, allTools.Count - mcpTools.Count);

        if (!string.IsNullOrWhiteSpace(sessionId))
            await agentStatusNotifier.NotifyAsync(sessionId, agent.Id, "running", ct);

        bool succeeded = false;
        try
        {
            IChatClient client = BuildClient(provider, allTools.Count > 0);
            ChatOptions chatOptions = BuildChatOptions(allTools, provider);
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

        List<ChatMessage> messages = BuildChatMessages(agent, history);

        // 按工具配置过滤要连接的 MCP Server
        IReadOnlyList<McpServerConfig> enabledMcpServers = FilterMcpServers(agent);
        var (mcpTools, connections) = await ToolRegistry.LoadToolsAsync(enabledMcpServers, loggerFactory, ct);

        // 过滤 MCP 工具中被单独禁用的工具
        IEnumerable<McpClientTool> filteredMcpTools = FilterMcpTools(agent, mcpTools);

        // 追加内置工具（当有 sessionId，且对应分组未禁用时）
        List<AITool> allTools = [.. filteredMcpTools];
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            allTools.AddRange(FilterCronTools(agent, CronTools.CreateForSession(sessionId, cronJobStore, cronScheduler)));
            // 追加技能工具
            allTools.AddRange(skillToolFactory.CreateTools(agent.BoundSkillIds, sessionId));
            // 追加子代理工具
            allTools.AddRange(SubAgentTools.CreateForSession(sessionId, agentStore, subAgentRunner));
        }
        else
        {
            _logger.LogDebug("sessionId 为空，跳过 CronJob/Skill/SubAgent 工具加载，Agent={AgentId}", agent.Id);
        }

        _logger.LogInformation("Agent {AgentId} streaming with {ToolCount} tools ({McpCount} MCP + {CronCount} built-in)",
            agent.Id, allTools.Count, mcpTools.Count, allTools.Count - mcpTools.Count);

        if (!string.IsNullOrWhiteSpace(sessionId))
            await agentStatusNotifier.NotifyAsync(sessionId, agent.Id, "running", ct);

        bool succeeded = false;
        UsageDetails? lastUsage = null;
        ChatResponseUpdate? lastUpdate = null;
        try
        {
            IChatClient client = BuildClient(provider, allTools.Count > 0);
            ChatOptions chatOptions = BuildChatOptions(allTools, provider);

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

    /// <summary>返回未被整体禁用的 MCP Server 配置列表。</summary>
    private static IReadOnlyList<McpServerConfig> FilterMcpServers(AgentConfig agent)
    {
        if (agent.ToolGroupConfigs.Count == 0) return agent.McpServers;
        return agent.McpServers
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

    /// <summary>根据 cron 分组配置过滤内置定时工具。</summary>
    private static IEnumerable<AIFunction> FilterCronTools(AgentConfig agent, IReadOnlyList<AIFunction> cronTools)
    {
        ToolGroupConfig? cronCfg = agent.ToolGroupConfigs.FirstOrDefault(g => g.GroupId == "cron");
        if (cronCfg is not null && !cronCfg.IsEnabled) return [];
        if (cronCfg is null) return cronTools;
        return cronTools.Where(t => !cronCfg.DisabledToolNames.Contains(t.Name));
    }

    private List<ChatMessage> BuildChatMessages(AgentConfig agent, IReadOnlyList<SessionMessage> history)
    {
        string dnaContext = dnaService.BuildSystemPromptContext(agent.Id);

        if (!string.IsNullOrEmpty(dnaContext))
        {
            int dnaBytes = System.Text.Encoding.UTF8.GetByteCount(dnaContext);
            _logger.LogDebug("DNA 上下文注入：{Bytes} 字节，Agent={AgentId}", dnaBytes, agent.Id);
        }

        string systemPrompt = string.IsNullOrWhiteSpace(dnaContext)
            ? agent.SystemPrompt
            : agent.SystemPrompt + "\n\n" + dnaContext;

        var messages = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new ChatMessage(ChatRole.System, systemPrompt));

        foreach (SessionMessage msg in history)
        {
            ChatRole role = msg.Role == "user" ? ChatRole.User : ChatRole.Assistant;
            messages.Add(new ChatMessage(role, msg.Content));
        }

        return messages;
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

    private static ChatOptions BuildChatOptions(IReadOnlyList<AITool> tools, ProviderConfig provider)
    {
        var options = new ChatOptions
        {
            ModelId = provider.ModelName,
            MaxOutputTokens = provider.MaxOutputTokens,
        };
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
