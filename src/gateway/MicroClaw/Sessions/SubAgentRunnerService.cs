using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MicroClaw.Agent;
using AgentEntity = MicroClaw.Agent.Agent;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;
using MicroClaw.Channels;
using MicroClaw.Configuration;
using MicroClaw.Configuration.Options;
using MicroClaw.Utils;

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
    private AgentStore AgentStore => sp.GetRequiredService<AgentStore>();
    private AgentRunner AgentRunner => sp.GetRequiredService<AgentRunner>();

    public async Task<string> RunSubAgentAsync(
        string agentId,
        string task,
        string sessionId,
        CancellationToken ct = default)
    {
        AgentEntity? agent = AgentStore.GetAgentById(agentId);
        if (agent is null)
            throw new InvalidOperationException($"子代理 '{agentId}' 不存在。");
        if (!agent.IsEnabled)
            throw new InvalidOperationException($"子代理 '{agent.Name}' 未启用。");
        
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
        string providerId = session?.ProviderId ?? string.Empty;
        string rootSessionId = currentRunContext?.RootSessionId ?? sessionId;
        string runId = Guid.NewGuid().ToString("N");
        var nestedRunContext = new SubAgentRunContext(rootSessionId, [.. ancestorAgentIds, agentId]);
        SubAgentRunContext? previousRunContext = SubAgentRunScope.Current;

        try
        {
            SessionMessage userMsg = new(Guid.NewGuid().ToString("N"), "user", task, null, DateTimeOffset.UtcNow, null,
                Source: $"sub-agent:{agentId}");
            var rootUserMeta = BuildSubAgentMetadata(agentId, agent.Name, runId);
            Sessions.AddMessage(rootSessionId,
                userMsg with { Id = Guid.NewGuid().ToString("N"), Metadata = rootUserMeta, Visibility = MessageVisibility.Internal });

            ChannelWriter<StreamItem>? parentWriter = SubAgentEventBridge.Current;

            if (parentWriter is not null)
                await parentWriter.WriteAsync(new SubAgentStartItem(agentId, agent.Name, task, runId), ct);

            var sw = Stopwatch.StartNew();
            StringBuilder textBuilder = new();
            StringBuilder thinkBuilder = new();
            List<ResponseAttachment> attachmentsList = [];

            SubAgentRunScope.Current = nestedRunContext;
            try
            {
                await foreach (StreamItem item in AgentRunner.StreamReActAsync(
                    agent,
                    providerId,
                    [userMsg],
                    rootSessionId,
                    ct,
                    source: "subagent",
                    ancestorAgentIdsOverride: ancestorAgentIds).WithCancellation(ct))
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

                        case ToolResultItem toolResult when parentWriter is not null:
                            string status = toolResult.Success ? $"✓ {toolResult.DurationMs}ms" : "✗ 失败";
                            await parentWriter.WriteAsync(
                                new SubAgentProgressItem(agentId, $"{toolResult.ToolName} {status}", runId), ct);
                            break;
                    }
                }
            }
            finally
            {
                SubAgentRunScope.Current = previousRunContext;
            }

            sw.Stop();

            (string extractedThink, string main) = ThinkContentParser.Extract(textBuilder.ToString());
            string? think = thinkBuilder.Length > 0
                ? (string.IsNullOrWhiteSpace(extractedThink) ? thinkBuilder.ToString() : thinkBuilder + "\n" + extractedThink)
                : (string.IsNullOrWhiteSpace(extractedThink) ? null : extractedThink);

            if (parentWriter is not null)
                await parentWriter.WriteAsync(
                    new SubAgentResultItem(agentId, agent.Name, main, sw.ElapsedMilliseconds, runId), ct);

            List<MessageAttachment>? attachments = attachmentsList.Count > 0
                ? attachmentsList.Select(a => new MessageAttachment(
                    a.FileName ?? "attachment", a.MimeType, Convert.ToBase64String(a.Data))).ToList()
                : null;
            
            SessionMessage assistantMsg = new(Guid.NewGuid().ToString("N"), "assistant", main, think,
                DateTimeOffset.UtcNow, attachments, Source: $"sub-agent:{agentId}");
            var rootAssistantMeta = BuildSubAgentMetadata(agentId, agent.Name, runId);
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

