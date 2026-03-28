using System.Diagnostics;
using MicroClaw.Gateway.Contracts.Streaming;
using MicroClaw.Providers;
using Microsoft.Extensions.Logging;

namespace MicroClaw.Agent.Workflows;

/// <summary>
/// 工作流执行引擎：按拓扑顺序遍历有向图节点，依次执行各节点，
/// 并通过 IAsyncEnumerable{StreamItem} 实时输出事件流。
/// 使用运行时上下文（currentAgentId / currentProviderId）实现代理和模型的传播。
/// </summary>
public sealed class WorkflowEngine(
    AgentStore agentStore,
    ProviderConfigStore providerStore,
    AgentRunner agentRunner,
    ILogger<WorkflowEngine> logger)
{
    public async IAsyncEnumerable<StreamItem> ExecuteAsync(
        WorkflowConfig workflow,
        string userInput,
        string executionId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        yield return new WorkflowStartItem(workflow.Id, workflow.Name, executionId);

        List<WorkflowNodeConfig> orderedNodes = TopologicalSort(workflow);
        if (orderedNodes.Count == 0)
        {
            yield return new WorkflowErrorItem(executionId, string.Empty, "工作流没有可执行节点。");
            yield break;
        }

        // 运行时上下文：代理与模型在节点间传播
        string? currentAgentId = agentStore.GetDefault()?.Id;
        string? currentProviderId = workflow.DefaultProviderId ?? providerStore.GetDefault()?.Id;

        Dictionary<string, string> nodeOutputs = new();
        string currentInput = userInput;
        string finalResult = string.Empty;

        foreach (WorkflowNodeConfig node in orderedNodes)
        {
            if (ct.IsCancellationRequested) yield break;

            if (node.Type is WorkflowNodeType.Start or WorkflowNodeType.End)
            {
                if (node.Type == WorkflowNodeType.End)
                    finalResult = currentInput;
                continue;
            }

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
                    string? effectiveAgentId = node.AgentId ?? currentAgentId;
                    if (string.IsNullOrWhiteSpace(effectiveAgentId))
                    {
                        yield return new WorkflowErrorItem(executionId, node.NodeId, "未配置 Agent 且无默认代理可用。");
                        yield break;
                    }

                    if (!string.IsNullOrWhiteSpace(node.AgentId))
                        currentAgentId = node.AgentId;

                    await foreach (StreamItem item in ExecuteAgentNodeAsync(node, currentInput, effectiveAgentId, currentProviderId ?? string.Empty, ct))
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
                case WorkflowNodeType.Tool:
                {
                    await foreach (StreamItem item in ExecuteToolNodeAsync(node, currentInput, executionId, currentAgentId, ct))
                    {
                        if (item is TokenItem t)
                            outputBuilder.Append(t.Content);
                        yield return item;
                    }
                    nodeSucceeded = outputBuilder.Length > 0 || true;
                    break;
                }
                case WorkflowNodeType.SwitchModel:
                {
                    string? newProviderId = node.ProviderId;
                    if (!string.IsNullOrWhiteSpace(newProviderId))
                    {
                        currentProviderId = newProviderId;
                        yield return new WorkflowModelSwitchItem(executionId, node.NodeId, newProviderId);
                    }
                    outputBuilder.Append(currentInput);
                    nodeSucceeded = true;
                    break;
                }
                case WorkflowNodeType.Router:
                {
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
        string effectiveAgentId,
        string providerId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        AgentConfig? agent = agentStore.GetById(effectiveAgentId);
        if (agent is null || !agent.IsEnabled)
        {
            logger.LogWarning("工作流节点 {NodeId} 引用的 Agent '{AgentId}' 不存在或已禁用，跳过。",
                node.NodeId, effectiveAgentId);
            yield break;
        }

        var history = new List<Gateway.Contracts.Sessions.SessionMessage>
        {
            new(Id: Guid.NewGuid().ToString("N"), Role: "user", Content: input, ThinkContent: null, Timestamp: DateTimeOffset.UtcNow, Attachments: null)
        };

        await foreach (StreamItem item in agentRunner.StreamReActAsync(agent, providerId, history, sessionId: null, ct, "workflow"))
            yield return item;
    }

    private static string ExecuteFunctionNode(WorkflowNodeConfig node, string input)
    {
        string funcName = node.FunctionName ?? string.Empty;
        return funcName switch
        {
            "uppercase" => input.ToUpperInvariant(),
            "lowercase" => input.ToLowerInvariant(),
            "trim" => input.Trim(),
            _ => input
        };
    }

    private async IAsyncEnumerable<StreamItem> ExecuteToolNodeAsync(
        WorkflowNodeConfig node,
        string input,
        string executionId,
        string? currentAgentId,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        string toolName = node.FunctionName ?? string.Empty;
        string? toolAgentId = node.Config?.GetValueOrDefault("toolAgentId");

        if (string.IsNullOrWhiteSpace(toolAgentId))
        {
            logger.LogWarning("工作流 Tool 节点 {NodeId} 未配置 toolAgentId。", node.NodeId);
            yield return new TokenItem(input);
            yield break;
        }

        if (!string.IsNullOrWhiteSpace(currentAgentId) && toolAgentId != currentAgentId)
        {
            yield return new WorkflowWarningItem(executionId, node.NodeId,
                $"Tool 节点使用的 Agent '{toolAgentId}' 与当前上下文 Agent '{currentAgentId}' 不一致。");
        }

        string result = await agentRunner.InvokeToolAsync(toolAgentId, toolName, node.Config, input, ct);
        yield return new TokenItem(result);
    }

    /// <summary>拓扑排序：Kahn 算法（BFS 层序）。</summary>
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

        return result;
    }

    /// <summary>查找当前节点的第一条入边来源节点 ID。</summary>
    private static string? GetSourceNodeId(WorkflowConfig workflow, string targetNodeId) =>
        workflow.Edges.FirstOrDefault(e => e.TargetNodeId == targetNodeId)?.SourceNodeId;
}
