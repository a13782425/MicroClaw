using MicroClaw.Agent.Memory;
using MicroClaw.Agent.Tools;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Infrastructure;
using MicroClaw.Infrastructure.Data;
using MicroClaw.Providers;
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
    CronJobStore cronJobStore,
    ICronJobScheduler cronScheduler,
    ILoggerFactory loggerFactory) : IAgentMessageHandler
{
    private readonly ILogger<AgentRunner> _logger = loggerFactory.CreateLogger<AgentRunner>();

    // ── IAgentMessageHandler ────────────────────────────────────────────────

    public bool HasAgentForChannel(string channelId) =>
        agentStore.All.Any(a => a.IsEnabled && a.BoundChannelIds.Contains(channelId));

    public async Task<string> HandleMessageAsync(
        string channelId,
        string sessionId,
        IReadOnlyList<SessionMessage> history,
        CancellationToken ct = default)
    {
        AgentConfig? agent = agentStore.All
            .FirstOrDefault(a => a.IsEnabled && a.BoundChannelIds.Contains(channelId));

        if (agent is null)
            throw new InvalidOperationException($"No enabled agent found for channel '{channelId}'.");

        return await RunReActAsync(agent, history, sessionId, ct);
    }

    // ── 核心 ReAct 循环（非流式）────────────────────────────────────────────

    public async Task<string> RunReActAsync(
        AgentConfig agent,
        IReadOnlyList<SessionMessage> history,
        string? sessionId = null,
        CancellationToken ct = default)
    {
        ProviderConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == agent.ProviderId);
        if (provider is null || !provider.IsEnabled)
            throw new InvalidOperationException($"Provider '{agent.ProviderId}' not found or disabled.");

        List<ChatMessage> messages = BuildChatMessages(agent, history);
        var (mcpTools, connections) = await ToolRegistry.LoadToolsAsync(agent.McpServers, loggerFactory, ct);

        // 追加定时任务 AI 工具（当有 sessionId 时）
        List<AITool> allTools = [.. mcpTools];
        if (!string.IsNullOrWhiteSpace(sessionId))
            allTools.AddRange(CronTools.CreateForSession(sessionId, cronJobStore, cronScheduler));

        _logger.LogInformation("Agent {AgentId} loaded {ToolCount} tools ({McpCount} MCP + {CronCount} built-in)",
            agent.Id, allTools.Count, mcpTools.Count, allTools.Count - mcpTools.Count);

        try
        {
            IChatClient client = BuildClient(provider, allTools.Count > 0);
            ChatOptions? chatOptions = BuildChatOptions(allTools);
            ChatResponse response = await client.GetResponseAsync(messages, chatOptions, ct);
            return response.Text ?? "（无回复）";
        }
        finally
        {
            await DisposeConnectionsAsync(connections);
        }
    }

    // ── 流式 ReAct 循环（供 SSE API 使用）──────────────────────────────────

    public async IAsyncEnumerable<string> StreamReActAsync(
        AgentConfig agent,
        IReadOnlyList<SessionMessage> history,
        string? sessionId = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ProviderConfig? provider = providerStore.All.FirstOrDefault(p => p.Id == agent.ProviderId);
        if (provider is null || !provider.IsEnabled)
            throw new InvalidOperationException($"Provider '{agent.ProviderId}' not found or disabled.");

        List<ChatMessage> messages = BuildChatMessages(agent, history);
        var (mcpTools, connections) = await ToolRegistry.LoadToolsAsync(agent.McpServers, loggerFactory, ct);

        // 追加定时任务 AI 工具（当有 sessionId 时）
        List<AITool> allTools = [.. mcpTools];
        if (!string.IsNullOrWhiteSpace(sessionId))
            allTools.AddRange(CronTools.CreateForSession(sessionId, cronJobStore, cronScheduler));

        _logger.LogInformation("Agent {AgentId} streaming with {ToolCount} tools ({McpCount} MCP + {CronCount} built-in)",
            agent.Id, allTools.Count, mcpTools.Count, allTools.Count - mcpTools.Count);

        try
        {
            IChatClient client = BuildClient(provider, allTools.Count > 0);
            ChatOptions? chatOptions = BuildChatOptions(allTools);

            await foreach (ChatResponseUpdate update in
                client.GetStreamingResponseAsync(messages, chatOptions, ct))
            {
                string token = update.Text ?? string.Empty;
                if (!string.IsNullOrEmpty(token))
                    yield return token;
            }
        }
        finally
        {
            await DisposeConnectionsAsync(connections);
        }
    }

    // ── 私有辅助方法 ────────────────────────────────────────────────────────

    private List<ChatMessage> BuildChatMessages(AgentConfig agent, IReadOnlyList<SessionMessage> history)
    {
        string dnaContext = dnaService.BuildSystemPromptContext(agent.Id);
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
        return inner.AsBuilder()
            .UseFunctionInvocation(loggerFactory)
            .Build();
    }

    private static ChatOptions? BuildChatOptions(IReadOnlyList<AITool> tools)
    {
        if (tools.Count == 0) return null;
        return new ChatOptions { Tools = [.. tools] };
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
