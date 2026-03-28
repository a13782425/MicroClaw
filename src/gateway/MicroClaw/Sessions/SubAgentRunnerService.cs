using System.Diagnostics;
using System.Text;
using System.Threading.Channels;
using MicroClaw.Agent;
using MicroClaw.Configuration;
using MicroClaw.Gateway.Contracts;
using MicroClaw.Gateway.Contracts.Sessions;
using MicroClaw.Gateway.Contracts.Streaming;

namespace MicroClaw.Sessions;

/// <summary>
/// 子代理运行服务：创建子会话、调用 AgentRunner 执行 ReAct 循环、持久化对话消息。
/// 实现 ISubAgentRunner 接口，由 MicroClaw.Agent 层通过接口调用，避免循环依赖。
/// 使用 Lazy&lt;AgentRunner&gt; 打破 AgentRunner ↔ SubAgentRunnerService 的循环注册依赖。
/// </summary>
public sealed class SubAgentRunnerService(
    SessionStore sessionStore,
    AgentStore agentStore,
    Lazy<AgentRunner> agentRunnerLazy) : ISubAgentRunner
{
    private static readonly int MaxSubAgentDepth = MicroClawConfig.Get<AgentOptions>().SubAgentMaxDepth;

    private AgentRunner AgentRunner => agentRunnerLazy.Value;

    public async Task<string> RunSubAgentAsync(
        string agentId,
        string task,
        string parentSessionId,
        CancellationToken ct = default)
    {
        AgentConfig? agent = agentStore.GetById(agentId);
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
                // 该祖先本身也是子代理会话，计入深度
                depth++;
                if (depth >= MaxSubAgentDepth)
                    throw new InvalidOperationException(
                        $"子代理调用深度已达上限（{MaxSubAgentDepth}），禁止继续派生子代理。");
            }

            if (ancestor.AgentId == agentId)
                throw new InvalidOperationException(
                    $"检测到循环子代理调用：代理 '{agentId}' 已存在于当前调用链中，禁止循环调用。");

            cursor = ancestor.ParentSessionId;
        }

        // 获取父会话 ProviderId（子会话继承同一模型）
        SessionInfo? parentSession = sessionStore.Get(parentSessionId);
        string providerId = parentSession?.ProviderId ?? string.Empty;

        // 创建子会话（立即批准，无需人工审核）
        string parentShort = parentSessionId.Length > 8 ? parentSessionId[..8] : parentSessionId;
        string title = $"[子代理] {agent.Name} ← {parentShort}";
        SessionInfo subSession = sessionStore.Create(
            title, providerId, ChannelType.Web,
            agentId: agentId,
            parentSessionId: parentSessionId);
        sessionStore.Approve(subSession.Id);

        // 保存用户任务消息
        SessionMessage userMsg = new(Guid.NewGuid().ToString("N"), "user", task, null, DateTimeOffset.UtcNow, null);
        sessionStore.AddMessage(subSession.Id, userMsg);

        // 获取父 SSE 流的事件 Writer（如果有），用于发送进度事件
        ChannelWriter<StreamItem>? parentWriter = SubAgentEventBridge.Current;

        // 向父 SSE 流发送子代理开始事件
        if (parentWriter is not null)
        {
            await parentWriter.WriteAsync(
                new SubAgentStartItem(agentId, agent.Name, task, subSession.Id), ct);
        }

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

                // 子代理调用工具时，向父 SSE 流发送进度事件
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

        // 合并 ThinkingContent 与文本中的 <think> 标签
        (string extractedThink, string main) = ThinkContentParser.Extract(textBuilder.ToString());
        string? think = thinkBuilder.Length > 0
            ? (string.IsNullOrWhiteSpace(extractedThink) ? thinkBuilder.ToString() : thinkBuilder + "\n" + extractedThink)
            : (string.IsNullOrWhiteSpace(extractedThink) ? null : extractedThink);

        // 向父 SSE 流发送子代理完成事件
        if (parentWriter is not null)
        {
            await parentWriter.WriteAsync(
                new SubAgentResultItem(agentId, agent.Name, main, sw.ElapsedMilliseconds), ct);
        }

        // 保存 AI 回复，Source 标记来源，携带 ThinkContent 和多模态附件
        List<MessageAttachment>? attachments = attachmentsList.Count > 0
            ? attachmentsList.Select(a => new MessageAttachment(
                a.FileName ?? "attachment", a.MimeType, Convert.ToBase64String(a.Data))).ToList()
            : null;

        SessionMessage assistantMsg = new(Guid.NewGuid().ToString("N"), "assistant", main, think, DateTimeOffset.UtcNow, attachments,
            Source: $"sub-agent:{agentId}");
        sessionStore.AddMessage(subSession.Id, assistantMsg);

        return main;
    }
}
