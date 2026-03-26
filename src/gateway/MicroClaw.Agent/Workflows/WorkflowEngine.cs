using System.Diagnostics;
using MicroClaw.Gateway.Contracts.Streaming;
using MicroClaw.Providers;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.Workflows;

/// <summary>
/// 工作流执行引擎：按拓扑顺序遍历有向图节点，依次（或并行）执行各节点，
/// 并通过 IAsyncEnumerable{StreamItem} 实时输出事件流。
/// 不依赖外部 Workflows 包，使用现有 AgentRunner 驱动每个 Agent 节点。
/// </summary>
public sealed class WorkflowEngine(
    AgentStore agentStore,
    ProviderConfigStore providerStore,
    AgentRunner agentRunner,
    ILogger<WorkflowEngine> logger)
{
    /// <summary>
    /// 流式执行工作流：遍历 DAG 节点，逐节点产生 WorkflowNodeStartItem / StreamItem / WorkflowNodeCompleteItem，
    /// 最终产生 WorkflowCompleteItem。
    /// </summary>
    public async IAsyncEnumerable<StreamItem> ExecuteAsync(
        WorkflowConfig workflow,
        string userInput,
        string executionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        yield return new WorkflowStartItem(workflow.Id, workflow.Name, executionId);

        // ── 拓扑排序节点列表 ──────────────────────────────────────────────
        List<WorkflowNodeConfig> orderedNodes = TopologicalSort(workflow);
        if (orderedNodes.Count == 0)
        {
            yield return new WorkflowErrorItem(executionId, string.Empty, "工作流没有可执行节点。");
            yield break;
        }

        // 节点输出暂存：nodeId → 输出文本（用于传递给下个节点）
        Dictionary<string, string> nodeOutputs = new();
        string currentInput = userInput;
        string finalResult = string.Empty;

        // ── 顺序执行各节点 ───────────────────────────────────────────────
        foreach (WorkflowNodeConfig node in orderedNodes)
        {
            if (ct.IsCancellationRequested) yield break;

            // 跳过 Start / End 控制节点（无实际执行逻辑）
            if (node.Type is WorkflowNodeType.Start or WorkflowNodeType.End)
            {
                if (node.Type == WorkflowNodeType.End)
                    finalResult = currentInput;
                continue;
            }

            // 发出前置告知边（前一节点 → 当前节点）
            string? sourceNodeId = GetSourceNodeId(workflow, node.NodeId);
            if (sourceNodeId is not null)
                yield return new WorkflowEdgeItem(executionId, sourceNodeId, node.NodeId, null);

            yield return new WorkflowNodeStartItem(executionId, node.NodeId, node.Label, node.Type.ToString());

            var nodeSw = Stopwatch.StartNew();
            bool nodeSucceeded = false;
            var outputBuilder = new System.Text.StringBuilder();

            switch (node.Type)
            {
                case WorkflowNodeType.Agent:
                {
                    // 使用 AgentRunner 流式执行（透传 token 流）
                    await foreach (StreamItem item in ExecuteAgentNodeAsync(node, currentInput, ct))
                    {
                        if (item is TokenItem t)
                            outputBuilder.Append(t.Content);
                        yield return item;
                    }
                    nodeSucceeded = true;
                    break;
                }
                case WorkflowNodeType.Function:
                {
                    string result = ExecuteFunctionNode(node, currentInput);
                    outputBuilder.Append(result);
                    yield return new TokenItem(result);
                    nodeSucceeded = true;
                    break;
                }
                case WorkflowNodeType.Router:
                {
                    // 路由节点：按条件选择下一条边（此处直接透传，条件路由在前端可视化处理）
                    outputBuilder.Append(currentInput);
                    nodeSucceeded = true;
                    break;
                }
            }

            nodeSw.Stop();
            string nodeOutput = outputBuilder.ToString();
            nodeOutputs[node.NodeId] = nodeOutput;
            currentInput = nodeOutput;
            finalResult = nodeOutput;

            if (nodeSucceeded)
            {
                yield return new WorkflowNodeCompleteItem(executionId, node.NodeId, nodeOutput, nodeSw.ElapsedMilliseconds);
            }
            else
            {
                yield return new WorkflowErrorItem(executionId, node.NodeId, $"节点 '{node.Label}' 执行失败。");
                yield break;
            }
        }

        sw.Stop();
        yield return new WorkflowCompleteItem(executionId, finalResult, sw.ElapsedMilliseconds);
    }

    // ── 私有辅助 ──────────────────────────────────────────────────────────

    private async IAsyncEnumerable<StreamItem> ExecuteAgentNodeAsync(
        WorkflowNodeConfig node,
        string input,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(node.AgentId))
        {
            logger.LogWarning("工作流 Agent 节点 {NodeId} 未配置 AgentId，跳过。", node.NodeId);
            yield break;
        }

        AgentConfig? agent = agentStore.GetById(node.AgentId);
        if (agent is null || !agent.IsEnabled)
        {
            logger.LogWarning("工作流节点 {NodeId} 引用的 Agent '{AgentId}' 不存在或已禁用，跳过。",
                node.NodeId, node.AgentId);
            yield break;
        }

        string providerId = providerStore.GetDefault()?.Id ?? string.Empty;

        var history = new List<Gateway.Contracts.Sessions.SessionMessage>
        {
            new(Role: "user", Content: input, ThinkContent: null, Timestamp: DateTimeOffset.UtcNow, Attachments: null)
        };

        await foreach (StreamItem item in agentRunner.StreamReActAsync(agent, providerId, history, sessionId: null, ct, "workflow"))
            yield return item;
    }

    private static string ExecuteFunctionNode(WorkflowNodeConfig node, string input)
    {
        // 内置函数节点的简单实现（可扩展为插件机制）
        string funcName = node.FunctionName ?? string.Empty;
        return funcName switch
        {
            "uppercase" => input.ToUpperInvariant(),
            "lowercase" => input.ToLowerInvariant(),
            "trim" => input.Trim(),
            _ => input   // 未知函数透传输入
        };
    }

    /// <summary>
    /// 拓扑排序：Kahn 算法（BFS 层序），保证入度为 0 的节点先执行。
    /// 对于没有边的工作流，按 Nodes 列表自然顺序返回。
    /// </summary>
    private static List<WorkflowNodeConfig> TopologicalSort(WorkflowConfig workflow)
    {
        Dictionary<string, WorkflowNodeConfig> nodeMap = workflow.Nodes.ToDictionary(n => n.NodeId);
        Dictionary<string, int> inDegree = workflow.Nodes.ToDictionary(n => n.NodeId, _ => 0);
        Dictionary<string, List<string>> adjacency = workflow.Nodes.ToDictionary(n => n.NodeId, _ => new List<string>());

        foreach (WorkflowEdgeConfig edge in workflow.Edges)
        {
            if (adjacency.ContainsKey(edge.SourceNodeId) && inDegree.ContainsKey(edge.TargetNodeId))
            {
                adjacency[edge.SourceNodeId].Add(edge.TargetNodeId);
                inDegree[edge.TargetNodeId]++;
            }
        }

        Queue<string> queue = new();
        foreach (var kv in inDegree.Where(kv => kv.Value == 0))
            queue.Enqueue(kv.Key);

        var result = new List<WorkflowNodeConfig>();
        while (queue.TryDequeue(out string? nodeId))
        {
            if (!nodeMap.TryGetValue(nodeId, out WorkflowNodeConfig? node)) continue;
            result.Add(node);
            foreach (string next in adjacency[nodeId])
            {
                inDegree[next]--;
                if (inDegree[next] == 0)
                    queue.Enqueue(next);
            }
        }

        // 如果图中有孤立节点（无边），fallback 到入度为 0 已在队列中处理。
        // 若结果数量少于节点总数说明存在环（不支持），返回已排序部分。
        return result;
    }

    /// <summary>查找当前节点的第一条入边来源节点 ID。</summary>
    private static string? GetSourceNodeId(WorkflowConfig workflow, string targetNodeId) =>
        workflow.Edges.FirstOrDefault(e => e.TargetNodeId == targetNodeId)?.SourceNodeId;
}
