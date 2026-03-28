using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using MicroClaw.Agent.ContextProviders;
using MicroClaw.Agent.Dev;
using MicroClaw.Agent.Sessions;
using MicroClaw.Agent.Middleware;
using MicroClaw.Agent.Streaming;
using MicroClaw.Agent.Streaming.Handlers;
using MicroClaw.Agent.Restorers;
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
    IEnumerable<IChatContentRestorer> chatContentRestorers) : IAgentMessageHandler
{
    private readonly ILogger<AgentRunner> _logger = loggerFactory.CreateLogger<AgentRunner>();
    private readonly IReadOnlyList<IAgentContextProvider> _contextProviders =
        contextProviders.OrderBy(p => p.Order).ToList().AsReadOnly();
    private readonly IReadOnlyList<IChatContentRestorer> _restorers =
        chatContentRestorers.ToList().AsReadOnly();

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

        SkillContext skillCtx = skillToolFactory.BuildSkillContext(agent.DisabledSkillIds, sessionId);
        List<ChatMessage> messages = await BuildChatMessagesAsync(agent, validatedHistory, sessionId, skillCtx.CatalogFragment, ct);

        // 按 Agent 配置收集所有工具（builtin + channel + skill + MCP），统一过滤
        SessionInfo? sessionForTools = !string.IsNullOrWhiteSpace(sessionId) ? sessionReader.Get(sessionId) : null;

        // 解析祖先链中的代理 ID（用于子代理工具排除循环调用）
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
        await using ToolCollectionResult toolResult = await toolCollector.CollectToolsAsync(agent, toolContext, ct);

        _logger.LogInformation("Agent {AgentId} streaming with {ToolCount} tools",
            agent.Id, toolResult.AllTools.Count);

        // ── AgentFactory：创建 ChatClientAgent + 事件 Channel ──────────────

        ChatOptions chatOptions = BuildChatOptions(toolResult.AllTools, provider, skillCtx.ModelOverride, skillCtx.EffortOverride);
        IChatClient rawClient = clientFactory.Create(provider);
        var (chatAgent, eventChannel, runOptions, tracker) = AgentFactory.Create(rawClient, agent.Name, chatOptions, loggerFactory, devMetrics: devMetrics);

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
                        // 从 AgentResponseUpdate 提取 MessageId，供 FunctionInvoker 共享
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
}
