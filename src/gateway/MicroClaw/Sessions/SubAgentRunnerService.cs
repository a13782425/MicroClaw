using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MicroClaw.Agent;
using AgentEntity = MicroClaw.Agent.Agent;
using MicroClaw.Abstractions;
using MicroClaw.Abstractions.Sessions;
using MicroClaw.Abstractions.Streaming;

namespace MicroClaw.Sessions;

/// <summary>
/// 子代理运行服务：复用或创建子会话、调用 AgentRunner 执行 ReAct 循环、持久化对话消息。
/// 实现 ISubAgentRunner 接口，由 MicroClaw.Agent 层通过接口调用，避免循环依赖。
/// 使用 Lazy&lt;AgentRunner&gt; 打破 AgentRunner ↔ SubAgentRunnerService 的循环注册依赖。
/// 
/// 关键设计：
/// - 同一父会话 + 同一 AgentId 下优先复用空闲子代理会话，避免反复创建新会话。
/// - 子代理消息同时写入子会话（保留独立上下文）和根会话（供 RAG 归并流程使用）。
/// - 子代理会话不在 GET /api/sessions 中对外暴露。
/// </summary>
public sealed class SubAgentRunnerService(
    SessionStore sessionStore,
    AgentStore agentStore,
    Lazy<AgentRunner> agentRunnerLazy,
    int maxSubAgentDepth = 3) : ISubAgentRunner
{
    private readonly int _maxSubAgentDepth = maxSubAgentDepth;

    // 记录当前正在运行的子代理会话 ID，用于空闲检测（避免并发复用同一会话）
    private readonly ConcurrentDictionary<string, byte> _activeSessions = new();

    private AgentRunner AgentRunner => agentRunnerLazy.Value;

    public async Task<string> RunSubAgentAsync(
        string agentId,
        string task,
        string parentSessionId,
        CancellationToken ct = default)
    {
        AgentEntity? agent = agentStore.GetAgentById(agentId);
        if (agent is null)
            throw new InvalidOperationException($"子代理 '{agentId}' 不存在。");
        if (!agent.IsEnabled)
            throw new InvalidOperationException($"子代理 '{agent.Name}' 未启用。");
        
        // 深度检查 + 循环调用检测：沿父会话链向上遍历
        // depth 仅统计具有 ParentSessionId 的祖先会话（即子代理会话），
        // 不计入顶层用户会话，从而允许最多 MaxSubAgentDepth 层子代理嵌套。
        int depth = 0;
        string? cursor = parentSessionId;
        while (cursor is not null)
        {
            SessionInfo? ancestor = sessionStore.Get(cursor);
            if (ancestor is null) break;

            if (ancestor.ParentSessionId is not null)
            {
                depth++;
                if (depth >= _maxSubAgentDepth)
                    throw new InvalidOperationException(
                        $"子代理调用深度已达上限（{_maxSubAgentDepth}），禁止继续派生子代理。");
            }

            if (ancestor.AgentId == agentId)
                throw new InvalidOperationException(
                    $"检测到循环子代理调用：代理 '{agentId}' 已存在于当前调用链中，禁止循环调用。");

            cursor = ancestor.ParentSessionId;
        }

        // 获取父会话 ProviderId（子会话继承同一模型）
        SessionInfo? parentSession = sessionStore.Get(parentSessionId);
        string providerId = parentSession?.ProviderId ?? string.Empty;
        
        // 根会话 ID：沿祖先链找到顶层会话，子代理消息将同步写入其 jsonl 供 RAG 归并
        string rootSessionId = sessionStore.GetRootSessionId(parentSessionId);
        
        // 优先复用空闲子代理会话（同一父会话 + 同一 AgentId），避免重复创建
        IReadOnlyCollection<string> activeIds = _activeSessions.Keys.ToList();
        SessionInfo? idleSession = sessionStore.FindIdleSubAgentSession(parentSessionId, agentId, activeIds);
        SessionInfo subSession;
        if (idleSession is not null)
        {
            subSession = idleSession;
        }
        else
        {
            string parentShort = parentSessionId.Length > 8 ? parentSessionId[..8] : parentSessionId;
            string title = $"[子代理] {agent.Name} ← {parentShort}";
            subSession = sessionStore.Create(
                title, providerId, ChannelType.Web,
                agentId: agentId,
                parentSessionId: parentSessionId);
            sessionStore.Approve(subSession.Id);
        }
        
        // 标记为活跃，防止并发调用复用同一会话
        _activeSessions.TryAdd(subSession.Id, 0);

        try
        {
            // 保存用户任务消息到子会话
            SessionMessage userMsg = new(Guid.NewGuid().ToString("N"), "user", task, null, DateTimeOffset.UtcNow, null,
                Source: $"sub-agent:{agentId}");
            sessionStore.AddMessage(subSession.Id, userMsg);
            
            // 同步写入根会话：携带 Metadata 标注来源子会话，供 UI 显示子代理信息
            // 使用 Internal 可见性：LLM 和前端均不可见，但 MemorySummarizationJob 的 role 过滤仍会包含，
            // 使子代理对话内容写入根会话的 RAG 命名空间，而不会破坏 tool_call/tool_result 的分组结构。
            if (rootSessionId != subSession.Id)
            {
                var rootUserMeta = BuildSubAgentMetadata(agentId, agent.Name, subSession.Id);
                sessionStore.AddMessage(rootSessionId,
                    userMsg with { Id = Guid.NewGuid().ToString("N"), Metadata = rootUserMeta, Visibility = MessageVisibility.Internal });
            }
            
            // 获取父 SSE 流的事件 Writer（如果有），用于发送进度事件
            ChannelWriter<StreamItem>? parentWriter = SubAgentEventBridge.Current;

            if (parentWriter is not null)
                await parentWriter.WriteAsync(new SubAgentStartItem(agentId, agent.Name, task, subSession.Id), ct);

            var sw = Stopwatch.StartNew();
            
            // 流式执行子 Agent ReAct 循环，同时向父 SSE 流转发进度事件
            StringBuilder textBuilder = new();
            StringBuilder thinkBuilder = new();
            List<ResponseAttachment> attachmentsList = [];

            await foreach (StreamItem item in AgentRunner.StreamReActAsync(agent, providerId, [userMsg], subSession.Id, ct, source: "subagent").WithCancellation(ct))
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
                            new SubAgentProgressItem(agentId, $"调用工具: {toolCall.ToolName}"), ct);
                        break;

                    case ToolResultItem toolResult when parentWriter is not null:
                        string status = toolResult.Success ? $"✓ {toolResult.DurationMs}ms" : "✗ 失败";
                        await parentWriter.WriteAsync(
                            new SubAgentProgressItem(agentId, $"{toolResult.ToolName} {status}"), ct);
                        break;
                }
            }

            sw.Stop();

            (string extractedThink, string main) = ThinkContentParser.Extract(textBuilder.ToString());
            string? think = thinkBuilder.Length > 0
                ? (string.IsNullOrWhiteSpace(extractedThink) ? thinkBuilder.ToString() : thinkBuilder + "\n" + extractedThink)
                : (string.IsNullOrWhiteSpace(extractedThink) ? null : extractedThink);

            if (parentWriter is not null)
                await parentWriter.WriteAsync(new SubAgentResultItem(agentId, agent.Name, main, sw.ElapsedMilliseconds), ct);

            List<MessageAttachment>? attachments = attachmentsList.Count > 0
                ? attachmentsList.Select(a => new MessageAttachment(
                    a.FileName ?? "attachment", a.MimeType, Convert.ToBase64String(a.Data))).ToList()
                : null;
            
            // 保存 AI 回复到子会话
            SessionMessage assistantMsg = new(Guid.NewGuid().ToString("N"), "assistant", main, think,
                DateTimeOffset.UtcNow, attachments, Source: $"sub-agent:{agentId}");
            sessionStore.AddMessage(subSession.Id, assistantMsg);
            
            // 同步写入根会话：携带 Metadata 标注来源子会话
            // Internal 可见性确保不破坏根会话的 tool_call/tool_result 分组，同时让 RAG 归并流程可捕获
            if (rootSessionId != subSession.Id)
            {
                var rootAssistantMeta = BuildSubAgentMetadata(agentId, agent.Name, subSession.Id);
                sessionStore.AddMessage(rootSessionId,
                    assistantMsg with { Id = Guid.NewGuid().ToString("N"), Metadata = rootAssistantMeta, Visibility = MessageVisibility.Internal });
            }

            return main;
        }
        finally
        {
            _activeSessions.TryRemove(subSession.Id, out _);
        }
    }
    
    /// <summary>构建写入根会话时附加的子代理来源元数据。</summary>
    private static IReadOnlyDictionary<string, JsonElement> BuildSubAgentMetadata(
        string agentId, string agentName, string subSessionId)
        => MetadataHelper.ToJsonElements(new Dictionary<string, object?>
        {
            ["agentId"] = agentId,
            ["agentName"] = agentName,
            ["subSessionId"] = subSessionId
        });
}

