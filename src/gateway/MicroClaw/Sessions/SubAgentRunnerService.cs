using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MicroClaw.Abstractions.Agent;
using MicroClaw.Agent;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Channels;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Providers;
using MicroClaw.Skills;
using MicroClaw.Tools;
using MicroClaw.Utils;
using Microsoft.Extensions.AI;

namespace MicroClaw.Sessions;

/// <summary>
/// 子代理运行服务：以一次性 SubAgentRun 方式调用 AgentRunner 执行任务，并将结果挂接到根会话。
/// 实现 ISubAgentRunner 接口，由 MicroClaw.Agent 层通过接口调用，避免循环依赖。
/// 通过 IServiceProvider 懒解析 AgentRunner，彻底消除 Lazy&lt;AgentRunner&gt; 循环依赖 hack。
/// </summary>
public sealed class SubAgentRunnerService(IServiceProvider sp) : ISubAgentRunner
{
    private int MaxSubAgentDepth => MicroClawConfig.Get<AgentsOptions>().SubAgentMaxDepth;
    private ISessionService Sessions => sp.GetRequiredService<ISessionService>();
    private IMicroAgentService AgentService => sp.GetRequiredService<IMicroAgentService>();
    private ProviderService ProviderSvc => sp.GetRequiredService<ProviderService>();
    private ChatMessageAssembler MessageAssembler => sp.GetRequiredService<ChatMessageAssembler>();
    private ToolCollector ToolCollector => sp.GetRequiredService<ToolCollector>();

    public async Task<string> RunSubAgentAsync(
        string agentId,
        string task,
        string sessionId,
        CancellationToken ct = default)
    {
        IMicroAgent? runtimeAgent = AgentService.GetById(agentId);
        if (runtimeAgent is null)
            throw new InvalidOperationException($"子代理 '{agentId}' 不存在。");
        if (!runtimeAgent.IsEnabled)
            throw new InvalidOperationException($"子代理 '{runtimeAgent.Name}' 未启用。");
        
        SubAgentRunContext? currentRunContext = SubAgentRunScope.Current;
        IReadOnlyList<string> ancestorAgentIds = currentRunContext?.AgentChain ?? Array.Empty<string>();
        if (ancestorAgentIds.Count >= MaxSubAgentDepth)
            throw new InvalidOperationException(
                $"子代理调用深度已达上限（{MaxSubAgentDepth}），禁止继续派生子代理。");
        if (ancestorAgentIds.Contains(agentId, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"检测到循环子代理调用：代理 '{agentId}' 已存在于当前调用链中，禁止循环调用。");

        // 获取父会话 ProviderId（子运行默认继承当前会话模型）
        IMicroSession? session = Sessions.Get(sessionId);
        string primaryProviderId = !string.IsNullOrWhiteSpace(session?.ProviderId)
            ? session.ProviderId
            : ProviderSvc.GetDefault()?.Id ?? string.Empty;
        string rootSessionId = currentRunContext?.RootSessionId ?? sessionId;
        string runId = Guid.NewGuid().ToString("N");
        var nestedRunContext = new SubAgentRunContext(rootSessionId, [.. ancestorAgentIds, agentId]);
        SubAgentRunContext? previousRunContext = SubAgentRunScope.Current;

        try
        {
            SessionMessage userMsg = new(Guid.NewGuid().ToString("N"), "user", task, null, DateTimeOffset.UtcNow, null,
                Source: $"sub-agent:{agentId}");
            var rootUserMeta = BuildSubAgentMetadata(agentId, runtimeAgent.Name, runId);
            Sessions.AddMessage(rootSessionId,
                userMsg with { Id = Guid.NewGuid().ToString("N"), Metadata = rootUserMeta, Visibility = MessageVisibility.Internal });

            ChannelWriter<StreamItem>? parentWriter = SubAgentEventBridge.Current;

            if (parentWriter is not null)
                await parentWriter.WriteAsync(new SubAgentStartItem(agentId, runtimeAgent.Name, task, runId), ct);

            var sw = Stopwatch.StartNew();
            StringBuilder textBuilder = new();
            StringBuilder thinkBuilder = new();
            List<ResponseAttachment> attachmentsList = [];

            // 获取 ProviderConfig 用于消息装配
            ProviderEntity? providerCfg = ProviderSvc.GetById(primaryProviderId)
                ?? ProviderSvc.GetDefault();
            if (providerCfg is null)
                throw new InvalidOperationException("找不到可用的模型提供方。");

            // 装配消息
            ChatMessageAssemblyResult assembly = await MessageAssembler.AssembleAsync(
                runtimeAgent, providerCfg, [userMsg], rootSessionId, ct: ct);

            // 收集工具（含子代理工具，传入祖先链）
            var toolCtx = new ToolCreationContext(
                SessionId: rootSessionId,
                CallingAgentId: agentId,
                DisabledSkillIds: runtimeAgent.DisabledSkillIds,
                AllowedSubAgentIds: runtimeAgent.AllowedSubAgentIds,
                AncestorAgentIds: ancestorAgentIds);

            ToolCollectionResult? toolResult = null;
            try
            {
                toolResult = await ToolCollector.CollectToolsAsync(runtimeAgent, toolCtx, ct);

                // 计算内部工具名称集
                var availableToolNames = toolResult.AllTools
                    .Select(static t => t.Name)
                    .Where(static n => !string.IsNullOrWhiteSpace(n))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                IReadOnlySet<string> internalToolNames = SkillToolProvider.InternalToolNames
                    .Where(availableToolNames.Contains)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                // 构建 ChatOptions
                ChatOptions executionOptions = ChatExecutionOptionsFactory.Build(toolResult.AllTools, providerCfg);

                // 构建完整 MicroChatContext
                IMicroSession contextSession = session ?? MicroChatContext.ForSystem(rootSessionId, "subagent", ct).Session;
                var chatContext = new MicroChatContext
                {
                    Session = contextSession,
                    Source = "subagent",
                    History = [userMsg],
                    Ct = ct,
                    TargetProviderId = providerCfg.Id,
                    AncestorAgentIds = ancestorAgentIds,
                    AssembledMessages = assembly.Messages,
                    AssembledTools = toolResult.AllTools,
                    InternalToolNames = internalToolNames,
                    ExecutionOptions = executionOptions,
                };

                SubAgentRunScope.Current = nestedRunContext;
                try
                {
                    await foreach (StreamItem item in runtimeAgent.StreamAsync(chatContext).WithCancellation(ct))
                    {
                        switch (item)
                        {
                            case TokenItem token:
                                textBuilder.Append(token.Content);
                                break;

                            case ThinkingItem thinking:
                                thinkBuilder.Append(thinking.Content);
                                break;

                            case DataContentItem data:
                                attachmentsList.Add(new ResponseAttachment(data.MimeType, data.Data));
                                break;

                            case ToolCallItem toolCall when parentWriter is not null:
                                await parentWriter.WriteAsync(
                                    new SubAgentProgressItem(agentId, $"调用工具: {toolCall.ToolName}", runId), ct);
                                break;

                            case ToolResultItem toolResultItem when parentWriter is not null:
                                string status = toolResultItem.Success ? $"✓ {toolResultItem.DurationMs}ms" : "✗ 失败";
                                await parentWriter.WriteAsync(
                                    new SubAgentProgressItem(agentId, $"{toolResultItem.ToolName} {status}", runId), ct);
                                break;
                        }
                    }
                }
                finally
                {
                    SubAgentRunScope.Current = previousRunContext;
                }
            }
            finally
            {
                if (toolResult is not null)
                    await toolResult.DisposeAsync();
            }

            sw.Stop();

            (string extractedThink, string main) = ThinkContentParser.Extract(textBuilder.ToString());
            string? think = thinkBuilder.Length > 0
                ? (string.IsNullOrWhiteSpace(extractedThink) ? thinkBuilder.ToString() : thinkBuilder + "\n" + extractedThink)
                : (string.IsNullOrWhiteSpace(extractedThink) ? null : extractedThink);

            if (parentWriter is not null)
                await parentWriter.WriteAsync(
                    new SubAgentResultItem(agentId, runtimeAgent.Name, main, sw.ElapsedMilliseconds, runId), ct);

            List<MessageAttachment>? attachments = attachmentsList.Count > 0
                ? attachmentsList.Select(a => new MessageAttachment(
                    a.FileName ?? "attachment", a.MimeType, Convert.ToBase64String(a.Data))).ToList()
                : null;
            
            SessionMessage assistantMsg = new(Guid.NewGuid().ToString("N"), "assistant", main, think,
                DateTimeOffset.UtcNow, attachments, Source: $"sub-agent:{agentId}");
            var rootAssistantMeta = BuildSubAgentMetadata(agentId, runtimeAgent.Name, runId);
            Sessions.AddMessage(rootSessionId,
                assistantMsg with { Id = Guid.NewGuid().ToString("N"), Metadata = rootAssistantMeta, Visibility = MessageVisibility.Internal });

            return main;
        }
        finally { SubAgentRunScope.Current = previousRunContext; }
    }
    
    /// <summary>构建写入根会话时附加的子代理来源元数据。</summary>
    private static IReadOnlyDictionary<string, JsonElement> BuildSubAgentMetadata(
        string agentId, string agentName, string runId)
        => MetadataHelper.ToJsonElements(new Dictionary<string, object?>
        {
            ["agentId"] = agentId,
            ["agentName"] = agentName,
            ["runId"] = runId
        });
}

